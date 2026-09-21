using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using SgoLogic.Pagos.Api.Application.Pagos;

namespace SgoLogic.Pagos.Api.Infrastructure.PayKlik;

/// <summary>
/// Disyuntor (circuit breaker) hacia PayKlik, segun ADR-009. Construye un
/// <see cref="ResiliencePipeline" /> explicito con SOLO la estrategia de circuit
/// breaker (Polly v8, via Microsoft.Extensions.Http.Resilience) y lo invoca
/// alrededor de la llamada HTTP al proveedor.
///
/// Cuentan como fallo: timeout (<see cref="TaskCanceledException" /> /
/// <see cref="TimeoutException" />), error de conexion
/// (<see cref="HttpRequestException" />) y respuestas 5xx
/// (<see cref="PayKlikNoDisponibleException" />). Un rechazo de negocio no lanza
/// excepcion: el proveedor respondio bien, asi que el circuito lo ve como exito.
///
/// No cuenta como fallo un token de medio de pago no valido
/// (<see cref="TokenMedioPagoInvalidoException" />, el 422 de PayKlik): es un error
/// del cliente y el proveedor esta arriba. Como ninguna de las condiciones de
/// arriba lo cubre, el circuito lo ve como una respuesta del proveedor (exito) y la
/// excepcion sube sin tocar la ventana de fallos.
///
/// Es singleton: el estado del circuito y la ventana de fallos recientes viven
/// entre solicitudes.
/// </summary>
public sealed class DisyuntorPayKlik
{
    private readonly ResiliencePipeline _pipeline;
    private readonly CircuitBreakerStateProvider _estadoCircuito = new();
    private readonly TimeSpan _ventana;
    private readonly object _bloqueo = new();
    private readonly Queue<DateTime> _fallosRecientes = new();
    private DateTime? _ultimaApertura;

    public DisyuntorPayKlik(IOptions<PayKlikOptions> opciones)
    {
        var configuracion = opciones.Value;
        _ventana = TimeSpan.FromSeconds(Math.Max(1, configuracion.VentanaSegundos));

        _pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = configuracion.RatioFallo,
                MinimumThroughput = Math.Max(2, configuracion.MinimoLlamadas),
                SamplingDuration = _ventana,
                BreakDuration = TimeSpan.FromSeconds(Math.Max(1, configuracion.AperturaSegundos)),
                StateProvider = _estadoCircuito,
                ShouldHandle = new PredicateBuilder()
                    .Handle<PayKlikNoDisponibleException>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                OnOpened = _ =>
                {
                    lock (_bloqueo)
                    {
                        _ultimaApertura = DateTime.UtcNow;
                    }

                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Ejecuta la operacion HTTP a traves del circuit breaker. Si el circuito esta
    /// abierto no invoca la operacion y lanza <see cref="CircuitoAbiertoException" />.
    /// </summary>
    public async Task<T> EjecutarAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion)
    {
        try
        {
            return await _pipeline.ExecuteAsync(
                static async (estado, token) => await estado(token),
                operacion,
                cancelacion);
        }
        catch (BrokenCircuitException ex)
        {
            throw new CircuitoAbiertoException(
                "El disyuntor de PayKlik esta abierto; no se invoco al proveedor.", ex);
        }
        catch (Exception ex) when (EsFalloDeProveedor(ex, cancelacion))
        {
            RegistrarFallo();
            throw;
        }
    }

    /// <summary>Estado del circuito, fallos recientes en la ventana y hora de la ultima apertura.</summary>
    public SaludProveedorDto Salud()
    {
        lock (_bloqueo)
        {
            PurgarVentana();
            return new SaludProveedorDto(
                NombreEstado(_estadoCircuito.CircuitState),
                _fallosRecientes.Count,
                _ultimaApertura);
        }
    }

    private static bool EsFalloDeProveedor(Exception ex, CancellationToken cancelacion) => ex switch
    {
        PayKlikNoDisponibleException => true,
        HttpRequestException => true,
        TimeoutException => true,
        OperationCanceledException => !cancelacion.IsCancellationRequested,
        _ => false
    };

    private void RegistrarFallo()
    {
        lock (_bloqueo)
        {
            _fallosRecientes.Enqueue(DateTime.UtcNow);
            PurgarVentana();
        }
    }

    private void PurgarVentana()
    {
        var limite = DateTime.UtcNow - _ventana;
        while (_fallosRecientes.Count > 0 && _fallosRecientes.Peek() < limite)
        {
            _fallosRecientes.Dequeue();
        }
    }

    private static string NombreEstado(CircuitState estado) => estado switch
    {
        CircuitState.Closed => "CERRADO",
        CircuitState.HalfOpen => "SEMIABIERTO",
        _ => "ABIERTO"
    };
}
