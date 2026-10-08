using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>Seccion Credito de Ordenes: timeout de la evaluacion en la saga.</summary>
public sealed class CreditoSagaOptions
{
    public const string Seccion = "Credito";

    /// <summary>Segundos maximos en EVALUACION_CREDITO_SOLICITADA antes de rechazar por falta de respuesta.</summary>
    public int TimeoutEvaluacionSegundos { get; set; } = 30;

    /// <summary>Cada cuanto revisa el worker.</summary>
    public int IntervaloTimeoutSegundos { get; set; } = 5;
}

public static class MetricasCreditoOrdenes
{
    public static readonly Counter TimeoutsTotal = Metrics.CreateCounter(
        "ordenes_credito_timeouts_total",
        "Ordenes a credito rechazadas porque Credito no respondio dentro del timeout de la saga.");
}

/// <summary>
/// Timeout de la ruta de credito: una orden que lleva mas de
/// Credito:TimeoutEvaluacionSegundos en EVALUACION_CREDITO_SOLICITADA pasa a
/// RECHAZADA como compensacion (OrdenCancelada con CREDITO_SIN_RESPUESTA y
/// LiberacionReservaSolicitada con TIMEOUT_SAGA). Logica sin BackgroundService para
/// poder probarla; la invoca <c>TimeoutEvaluacionCreditoWorker</c>.
///
/// Convive con una aprobacion simultanea por el token de concurrencia de la orden
/// (Secuencia): si la aprobacion guarda primero, aqui la transicion choca y se
/// abandona el ciclo (el contexto queda sucio); en el siguiente ciclo la orden ya no
/// esta en evaluacion. Si gana el timeout, la aprobacion llega sobre una orden
/// RECHAZADA, se descarta, y Credito anula el cargo al consumir OrdenCancelada.
/// </summary>
public sealed class ServicioTimeoutCredito
{
    public const string MotivoVisible =
        "No pudimos confirmar tu crédito a tiempo. No se realizó ningún cargo; puedes intentar de nuevo o pagar con tarjeta.";

    private const int LotePorCiclo = 50;

    private readonly IRepositorioOrdenes _ordenes;
    private readonly ServicioConfirmacionOrden _servicio;
    private readonly CreditoSagaOptions _opciones;
    private readonly ILogger<ServicioTimeoutCredito> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioTimeoutCredito(
        IRepositorioOrdenes ordenes,
        ServicioConfirmacionOrden servicio,
        IOptions<CreditoSagaOptions> opciones,
        ILogger<ServicioTimeoutCredito> logger)
        : this(ordenes, servicio, opciones, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioTimeoutCredito(
        IRepositorioOrdenes ordenes,
        ServicioConfirmacionOrden servicio,
        IOptions<CreditoSagaOptions> opciones,
        ILogger<ServicioTimeoutCredito> logger,
        Func<DateTime> reloj)
    {
        _ordenes = ordenes;
        _servicio = servicio;
        _opciones = opciones.Value;
        _logger = logger;
        _reloj = reloj;
    }

    /// <summary>Rechaza las evaluaciones vencidas; devuelve cuantas ordenes movio.</summary>
    public async Task<int> ProcesarVencidasAsync(CancellationToken cancelacion)
    {
        var limite = _reloj().AddSeconds(-_opciones.TimeoutEvaluacionSegundos);
        var vencidas = await _ordenes.ListarEvaluacionesCreditoVencidasAsync(limite, LotePorCiclo, cancelacion);

        var rechazadas = 0;
        foreach (var ordenId in vencidas)
        {
            var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion);
            if (orden?.Estado != EstadoOrden.EVALUACION_CREDITO_SOLICITADA)
            {
                continue;
            }

            try
            {
                await _servicio.AplicarTransicionAsync(
                    ordenId,
                    EstadoOrden.RECHAZADA,
                    PasoSaga.COBRO,
                    MotivoVisible,
                    esCompensacion: true,
                    cancelacion,
                    MotivoLiberacionReserva.TIMEOUT_SAGA);
            }
            catch (ExcepcionConflictoConcurrencia ex)
            {
                // La respuesta de Credito gano la carrera. El contexto quedo con la
                // transicion sin guardar: se corta el ciclo y el siguiente arranca limpio.
                _logger.LogInformation(
                    ex,
                    "La orden {OrdenId} cambio mientras vencia su evaluacion de credito; se deja como quedo.",
                    ordenId);
                break;
            }
            catch (ExcepcionTransicionInvalida ex)
            {
                _logger.LogInformation(ex, "La orden {OrdenId} ya salio de la evaluacion de credito.", ordenId);
                continue;
            }

            rechazadas++;
            MetricasCreditoOrdenes.TimeoutsTotal.Inc();
            _logger.LogWarning(
                "Orden {OrdenId} rechazada: Credito no respondio en {Segundos} s.",
                ordenId, _opciones.TimeoutEvaluacionSegundos);
        }

        return rechazadas;
    }
}
