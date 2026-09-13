using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IRegistroMensajesProcesados" />. Guarda los
/// messageId en un conjunto y deja ver que se registro. <see cref="Registrar" />
/// marca de una vez (igual que <see cref="OutboxWriterFalso" /> acumula sin base):
/// el detalle de que la fila real se persista recien en el SaveChanges se verifica
/// leyendo el codigo, no aca.
/// </summary>
public sealed class RegistroMensajesProcesadosFalso : IRegistroMensajesProcesados
{
    private readonly HashSet<string> _procesados = new();

    public List<(string MessageId, string TipoEvento)> Registrados { get; } = new();

    public void Precargar(string messageId) => _procesados.Add(messageId);

    public Task<bool> YaProcesadoAsync(string messageId, CancellationToken cancelacion) =>
        Task.FromResult(_procesados.Contains(messageId));

    public void Registrar(string messageId, string tipoEvento)
    {
        _procesados.Add(messageId);
        Registrados.Add((messageId, tipoEvento));
    }
}
