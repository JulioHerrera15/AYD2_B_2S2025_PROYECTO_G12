using SgoLogic.Pagos.Api.Application.Pagos;

namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IClientePayKlik" />. No toca el mock HTTP real:
/// devuelve un resultado fijo o lanza una falla, y registra cada peticion recibida.
/// </summary>
public sealed class ClientePayKlikFalso : IClientePayKlik
{
    private readonly Func<PeticionCargoPayKlik, Task<ResultadoPayKlik>> _comportamiento;

    private ClientePayKlikFalso(Func<PeticionCargoPayKlik, Task<ResultadoPayKlik>> comportamiento)
    {
        _comportamiento = comportamiento;
    }

    public List<PeticionCargoPayKlik> Peticiones { get; } = new();

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

    public async Task<ResultadoPayKlik> CargarAsync(PeticionCargoPayKlik peticion, CancellationToken cancelacion)
    {
        Peticiones.Add(peticion);
        AntesDeResponder?.Invoke();
        return await _comportamiento(peticion);
    }
}
