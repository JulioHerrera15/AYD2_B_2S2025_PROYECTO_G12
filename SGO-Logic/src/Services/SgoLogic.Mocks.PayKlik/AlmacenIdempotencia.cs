using System.Collections.Concurrent;

namespace SgoLogic.Mocks.PayKlik;

/// <summary>
/// Almacen en memoria de idempotencia del mock: mapea la clave del header
/// <c>Idempotency-Key</c> a la respuesta ya calculada para ese cargo. Ante una
/// clave repetida se devuelve la misma respuesta sin volver a procesar, para que
/// los reintentos del cliente no generen doble cargo.
/// </summary>
public sealed class AlmacenIdempotencia
{
    private readonly ConcurrentDictionary<string, RespuestaCargo> _respuestas = new();

    /// <summary>Cantidad de claves de idempotencia guardadas en esta ejecucion.</summary>
    public int Cantidad => _respuestas.Count;

    public bool TryObtener(string clave, out RespuestaCargo respuesta) =>
        _respuestas.TryGetValue(clave, out respuesta!);

    /// <summary>
    /// Devuelve la respuesta guardada para la clave; si no existe, la calcula con
    /// <paramref name="generar" />, la guarda y la devuelve. <paramref name="generar" />
    /// no se invoca cuando la clave ya estaba.
    /// </summary>
    public RespuestaCargo ObtenerOAgregar(string clave, Func<RespuestaCargo> generar) =>
        _respuestas.GetOrAdd(clave, _ => generar());
}
