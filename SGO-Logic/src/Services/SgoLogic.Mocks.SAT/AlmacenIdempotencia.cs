using System.Collections.Concurrent;

namespace SgoLogic.Mocks.SAT;

/// <summary>
/// Almacen en memoria de idempotencia del mock: mapea la clave del header
/// <c>Idempotency-Key</c> (el identificador del documento fiscal) a la respuesta ya
/// calculada para esa certificacion. Ante una clave repetida se devuelve la misma
/// respuesta sin volver a procesar, para que los reintentos del cliente no generen
/// una certificacion duplicada.
/// </summary>
public sealed class AlmacenIdempotencia
{
    private readonly ConcurrentDictionary<string, RespuestaCertificacion> _respuestas = new();

    /// <summary>Cantidad de claves de idempotencia guardadas en esta ejecucion.</summary>
    public int Cantidad => _respuestas.Count;

    /// <summary>Copia de las claves guardadas, para GET /admin/estado.</summary>
    public IReadOnlyList<string> Claves => _respuestas.Keys.ToArray();

    public bool TryObtener(string clave, out RespuestaCertificacion respuesta) =>
        _respuestas.TryGetValue(clave, out respuesta!);

    /// <summary>
    /// Devuelve la respuesta guardada para la clave; si no existe, la calcula con
    /// <paramref name="generar" />, la guarda y la devuelve. <paramref name="generar" />
    /// no se invoca cuando la clave ya estaba.
    /// </summary>
    public RespuestaCertificacion ObtenerOAgregar(string clave, Func<RespuestaCertificacion> generar) =>
        _respuestas.GetOrAdd(clave, _ => generar());
}
