namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Puerto de deduplicacion de eventos consumidos. La implementacion vive en
/// Infrastructure/Persistencia sobre la tabla <c>ordenes.mensaje_procesado</c>.
/// <see cref="Registrar" /> no persiste por su cuenta: la fila se guarda en el
/// mismo <c>SaveChangesAsync</c> que la transicion y el outbox.
/// </summary>
public interface IRegistroMensajesProcesados
{
    /// <summary>True si el <paramref name="messageId" /> ya fue aplicado antes.</summary>
    Task<bool> YaProcesadoAsync(string messageId, CancellationToken cancelacion);

    /// <summary>
    /// Anota el <paramref name="messageId" /> como procesado. Solo agrega la marca
    /// a la unidad de trabajo; no llama a <c>SaveChangesAsync</c>.
    /// </summary>
    void Registrar(string messageId, string tipoEvento);
}
