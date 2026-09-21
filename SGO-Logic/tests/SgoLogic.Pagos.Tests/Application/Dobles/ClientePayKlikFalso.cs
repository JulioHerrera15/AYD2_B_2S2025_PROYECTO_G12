using SgoLogic.Mocks.PayKlik;
using SgoLogic.Pagos.Api.Application.Pagos;

namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IClientePayKlik" />. No toca el mock HTTP real:
/// devuelve un resultado fijo o lanza una falla, y registra cada peticion recibida
/// junto con la clave de idempotencia con la que se invoco.
/// </summary>
public sealed class ClientePayKlikFalso : IClientePayKlik
{
    private readonly Func<PeticionCargoPayKlik, Task<ResultadoPayKlik>> _comportamiento;

    private ClientePayKlikFalso(Func<PeticionCargoPayKlik, Task<ResultadoPayKlik>> comportamiento)
    {
        _comportamiento = comportamiento;
    }

    public List<PeticionCargoPayKlik> Peticiones { get; } = new();

    public List<string> ClavesIdempotencia { get; } = new();

    /// <summary>
    /// Cuando es <c>false</c>, <see cref="CargarAsync" /> no registra la peticion:
    /// modela el circuito abierto, donde nunca se invoca al proveedor.
    /// </summary>
    public bool RegistraPeticiones { get; init; } = true;

    /// <summary>Hook que se ejecuta dentro de CargarAsync, justo antes de responder.</summary>
    public Action? AntesDeResponder { get; set; }

    public static ClientePayKlikFalso Aprueba(string autorizacionId = "PK-TEST-000001") =>
        new(_ => Task.FromResult(new ResultadoPayKlik(
            autorizacionId, EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado",
            "4242", "VISA", DateTime.UtcNow)));

    public static ClientePayKlikFalso Rechaza() =>
        new(_ => Task.FromResult(new ResultadoPayKlik(
            null, EstadoRespuestaPayKlik.RECHAZADO, "51", "Fondos insuficientes",
            "4242", "VISA", DateTime.UtcNow)));

    public static ClientePayKlikFalso Timeout() =>
        new(_ => Task.FromException<ResultadoPayKlik>(
            new TaskCanceledException("PayKlik no respondio (timeout simulado).")));

    public static ClientePayKlikFalso NoDisponible() =>
        new(_ => Task.FromException<ResultadoPayKlik>(
            new PayKlikNoDisponibleException("PayKlik respondio 503.")));

    /// <summary>Falla con 5xx las primeras <paramref name="fallas5xx" /> llamadas y aprueba a partir de ahi.</summary>
    public static ClientePayKlikFalso FallaLuegoAprueba(int fallas5xx, string autorizacionId = "PK-TEST-OK")
    {
        var restantes = fallas5xx;
        return new(_ =>
        {
            if (restantes-- > 0)
            {
                return Task.FromException<ResultadoPayKlik>(
                    new PayKlikNoDisponibleException("PayKlik respondio 503."));
            }

            return Task.FromResult(new ResultadoPayKlik(
                autorizacionId, EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado",
                "4242", "VISA", DateTime.UtcNow));
        });
    }

    /// <summary>
    /// PayKlik responde que el token no sirve (422 con un codigo TOKEN_*). El cliente
    /// real lo traduce a <see cref="TokenMedioPagoInvalidoException" />.
    /// </summary>
    public static ClientePayKlikFalso TokenNoValido(string codigo) =>
        new(_ => Task.FromException<ResultadoPayKlik>(
            new TokenMedioPagoInvalidoException(codigo, "PayKlik rechazo el token del medio de pago.")));

    /// <summary>
    /// Se comporta como el cobro del mock de PayKlik: resuelve el token contra un
    /// <see cref="AlmacenTokens" /> real, lo consume y devuelve los ultimos cuatro y
    /// la marca de la tarjeta tokenizada. Un token inexistente, vencido o ya usado
    /// lanza <see cref="TokenMedioPagoInvalidoException" />, igual que el 422.
    /// </summary>
    public static ClientePayKlikFalso ResuelveTokens(AlmacenTokens almacen) =>
        new(peticion =>
        {
            var consumo = almacen.Consumir(peticion.TokenMedioPago);

            if (consumo.Resultado != ResultadoConsumoToken.Consumido)
            {
                var codigo = consumo.Resultado switch
                {
                    ResultadoConsumoToken.Vencido => CodigosToken.Vencido,
                    ResultadoConsumoToken.YaUsado => CodigosToken.YaUsado,
                    _ => CodigosToken.Invalido
                };

                return Task.FromException<ResultadoPayKlik>(
                    new TokenMedioPagoInvalidoException(codigo, "PayKlik rechazo el token del medio de pago."));
            }

            return Task.FromResult(new ResultadoPayKlik(
                "PK-TEST-TOKEN", EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado",
                consumo.Tarjeta!.UltimosCuatro, consumo.Tarjeta.Marca, DateTime.UtcNow));
        });

    /// <summary>Simula el disyuntor abierto: lanza sin registrar la peticion.</summary>
    public static ClientePayKlikFalso CircuitoAbierto() =>
        new(_ => Task.FromException<ResultadoPayKlik>(
            new CircuitoAbiertoException("El disyuntor de PayKlik esta abierto (simulado).")))
        {
            RegistraPeticiones = false
        };

    public async Task<ResultadoPayKlik> CargarAsync(
        PeticionCargoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        if (RegistraPeticiones)
        {
            Peticiones.Add(peticion);
            ClavesIdempotencia.Add(claveIdempotencia);
        }

        AntesDeResponder?.Invoke();
        return await _comportamiento(peticion);
    }

    public Func<PeticionReembolsoPayKlik, Task<ResultadoPayKlik>>? ComportamientoReembolso { get; set; }

    public async Task<ResultadoPayKlik> ReembolsoAsync(
        PeticionReembolsoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        if (ComportamientoReembolso is not null)
        {
            return await ComportamientoReembolso(peticion);
        }

        return new ResultadoPayKlik(
            "PK-REEMBOLSO-000001", EstadoRespuestaPayKlik.APROBADO, "00", "Reembolso aprobado",
            null, null, DateTime.UtcNow);
    }
}
