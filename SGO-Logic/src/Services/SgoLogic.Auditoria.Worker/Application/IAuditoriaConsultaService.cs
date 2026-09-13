namespace SgoLogic.Auditoria.Worker.Application;

/// <summary>Lecturas de solo consulta sobre eventos_auditoria, paginadas.</summary>
public interface IAuditoriaConsultaService
{
    /// <summary>EAC-03: toda la evidencia de una transaccion, en orden cronologico, en menos de 1 segundo.</summary>
    Task<PaginaResultado<EventoAuditoriaDto>> ObtenerPorTransaccionAsync(
        string transactionId, int pagina, int tamano, CancellationToken cancelacion);

    Task<PaginaResultado<EventoAuditoriaDto>> ObtenerPorAgregadoAsync(
        string aggregateId, int pagina, int tamano, CancellationToken cancelacion);
}