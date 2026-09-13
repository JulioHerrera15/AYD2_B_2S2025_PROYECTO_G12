using System.Collections.Concurrent;
using SgoLogic.Facturacion.Api.Application.Facturacion;

namespace SgoLogic.Facturacion.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IClienteSat" />. No toca el mock HTTP real:
/// devuelve un resultado fijo o lanza una falla, registra cada peticion recibida y
/// la clave de idempotencia con la que se invoco, y opcionalmente respeta la
/// idempotencia (misma clave devuelve la misma respuesta sin volver a "certificar").
/// </summary>
public sealed class ClienteSatFalso : IClienteSat
{
    private readonly Func<PeticionCertificacionSat, Task<ResultadoCertificacionSat>> _comportamiento;
    private readonly ConcurrentDictionary<string, ResultadoCertificacionSat> _idempotencia = new();
    private readonly bool _respetaIdempotencia;

    private ClienteSatFalso(
        Func<PeticionCertificacionSat, Task<ResultadoCertificacionSat>> comportamiento,
        bool respetaIdempotencia = false)
    {
        _comportamiento = comportamiento;
        _respetaIdempotencia = respetaIdempotencia;
    }

    public List<PeticionCertificacionSat> Peticiones { get; } = new();

    public List<string> ClavesIdempotencia { get; } = new();

    /// <summary>Cuantas veces se ejecuto realmente el comportamiento (sin contar respuestas idempotentes reusadas).</summary>
    public int CertificacionesReales { get; private set; }

    public static ClienteSatFalso Certifica(string autorizacion = "AUTZ-000001", string folio = "FOL-000001") =>
        new(_ => Task.FromResult(new ResultadoCertificacionSat(
            EstadoCertificacionSat.CERTIFICADO, folio, autorizacion, "00", "Documento certificado", DateTime.UtcNow)));

    public static ClienteSatFalso CertificaIdempotente(string autorizacion = "AUTZ-IDEM") =>
        new(
            _ => Task.FromResult(new ResultadoCertificacionSat(
                EstadoCertificacionSat.CERTIFICADO,
                $"FOL-{Guid.NewGuid():N}"[..12],
                autorizacion,
                "00",
                "Documento certificado",
                DateTime.UtcNow)),
            respetaIdempotencia: true);

    public static ClienteSatFalso Rechaza() =>
        new(_ => Task.FromResult(new ResultadoCertificacionSat(
            EstadoCertificacionSat.RECHAZADO, null, null, "RECH-01", "Rechazado por la SAT", DateTime.UtcNow)));

    public static ClienteSatFalso NoDisponible() =>
        new(_ => Task.FromException<ResultadoCertificacionSat>(
            new SatNoDisponibleException("La SAT respondio 503.")));

    public static ClienteSatFalso Timeout() =>
        new(_ => Task.FromException<ResultadoCertificacionSat>(
            new TaskCanceledException("La SAT no respondio (timeout simulado).")));

    /// <summary>Rechaza las primeras <paramref name="rechazos" /> llamadas y certifica a partir de ahi.</summary>
    public static ClienteSatFalso RechazaLuegoCertifica(int rechazos, string autorizacion = "AUTZ-OK")
    {
        var restantes = rechazos;
        return new(_ =>
        {
            if (restantes-- > 0)
            {
                return Task.FromResult(new ResultadoCertificacionSat(
                    EstadoCertificacionSat.RECHAZADO, null, null, "RECH-01", "Rechazado por la SAT", DateTime.UtcNow));
            }

            return Task.FromResult(new ResultadoCertificacionSat(
                EstadoCertificacionSat.CERTIFICADO, "FOL-OK", autorizacion, "00", "Documento certificado", DateTime.UtcNow));
        });
    }

    public async Task<ResultadoCertificacionSat> CertificarAsync(
        PeticionCertificacionSat peticion,
        string claveIdempotencia,
        CancellationToken cancelacion)
    {
        Peticiones.Add(peticion);
        ClavesIdempotencia.Add(claveIdempotencia);

        if (_respetaIdempotencia && _idempotencia.TryGetValue(claveIdempotencia, out var previa))
        {
            return previa;
        }

        CertificacionesReales++;
        var resultado = await _comportamiento(peticion);

        if (_respetaIdempotencia)
        {
            _idempotencia.TryAdd(claveIdempotencia, resultado);
        }

        return resultado;
    }
}
