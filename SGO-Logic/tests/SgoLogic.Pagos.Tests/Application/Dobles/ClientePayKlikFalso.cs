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
    /// PayKlik no acepta los datos del medio de pago (422/404 con un codigo como
    /// TARJETA_INVALIDA o TARJETA_NO_ENCONTRADA). El cliente real lo traduce a
    /// <see cref="TokenMedioPagoInvalidoException" />.
    /// </summary>
    public static ClientePayKlikFalso TokenNoValido(string codigo) =>
        new(_ => Task.FromException<ResultadoPayKlik>(
            new TokenMedioPagoInvalidoException(codigo, "PayKlik rechazo el medio de pago.")));

    /// <summary>
    /// Se comporta como el cobro del mock de PayKlik: aprueba, devuelve los ultimos
    /// cuatro y la marca de la tarjeta recibida y, si se pidio guardar una tarjeta
    /// nueva, la referencia de boveda <paramref name="referenciaGuardada" />.
    /// </summary>
    public static ClientePayKlikFalso ApruebaComoPayKlik(string referenciaGuardada = "card_pk_nueva") =>
        new(peticion =>
        {
            var ultimos = peticion.Tarjeta?.Numero[^4..];
            return Task.FromResult(new ResultadoPayKlik(
                "PK-TEST-TARJETA", EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado",
                ultimos, ultimos is null ? null : "VISA", DateTime.UtcNow,
                ReferenciaTarjeta: peticion.Tarjeta is not null && peticion.Guardar == true ? referenciaGuardada : null));
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

    public List<PeticionReembolsoPayKlik> PeticionesReembolso { get; } = new();

    public List<string> ClavesReembolso { get; } = new();

    public async Task<ResultadoPayKlik> ReembolsoAsync(
        PeticionReembolsoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        PeticionesReembolso.Add(peticion);
        ClavesReembolso.Add(claveIdempotencia);

        if (ComportamientoReembolso is not null)
        {
            return await ComportamientoReembolso(peticion);
        }

        return new ResultadoPayKlik(
            "PK-REEMBOLSO-000001", EstadoRespuestaPayKlik.APROBADO, "00", "Reembolso aprobado",
            null, null, DateTime.UtcNow);
    }
}
