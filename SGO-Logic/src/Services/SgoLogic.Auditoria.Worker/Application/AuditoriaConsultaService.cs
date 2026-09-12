using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.Auditoria.Worker.Domain;
using SgoLogic.Auditoria.Worker.Infrastructure;

namespace SgoLogic.Auditoria.Worker.Application;

/// <summary>
/// Consultas de solo lectura contra eventos_auditoria. El indice por
/// TransactionId (y el orden por OccurredAt/Sequence, que aprovecha ese mismo
/// indice) es lo que sostiene EAC-03; ObtenerPorTransaccionAsync es el unico
/// metodo instrumentado con el histogram, porque es la consulta con la que se
/// demuestra ese escenario de calidad.
/// </summary>
public sealed class AuditoriaConsultaService : IAuditoriaConsultaService
{
    private const int TamanoMaximo = 200;

    private readonly AuditoriaDbContext _contexto;

    public AuditoriaConsultaService(AuditoriaDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<PaginaResultado<EventoAuditoriaDto>> ObtenerPorTransaccionAsync(
        string transactionId, int pagina, int tamano, CancellationToken cancelacion)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            return await ConsultarAsync(
                _contexto.EventosAuditoria.Where(e => e.TransactionId == transactionId),
                pagina, tamano, cancelacion);
        }
        finally
        {
            MetricasAuditoria.ConsultaEvidenciaSegundos.Observe(cronometro.Elapsed.TotalSeconds);
        }
    }

    public Task<PaginaResultado<EventoAuditoriaDto>> ObtenerPorAgregadoAsync(
        string aggregateId, int pagina, int tamano, CancellationToken cancelacion)
    {
        return ConsultarAsync(
            _contexto.EventosAuditoria.Where(e => e.AggregateId == aggregateId),
            pagina, tamano, cancelacion);
    }

    private static async Task<PaginaResultado<EventoAuditoriaDto>> ConsultarAsync(
        IQueryable<EventoAuditoria> consulta, int pagina, int tamano, CancellationToken cancelacion)
    {
        pagina = Math.Max(pagina, 1);
        tamano = Math.Clamp(tamano, 1, TamanoMaximo);

        var ordenada = consulta.OrderBy(e => e.OccurredAt).ThenBy(e => e.Sequence);

        var total = await ordenada.CountAsync(cancelacion);

        var filas = await ordenada
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(cancelacion);

        var items = filas.Select(AMapa).ToList();

        return new PaginaResultado<EventoAuditoriaDto>(items, pagina, tamano, total);
    }

    private static EventoAuditoriaDto AMapa(EventoAuditoria fila) => new(
        fila.EventId,
        fila.EventType,
        fila.EventVersion,
        fila.OccurredAt,
        fila.Sequence,
        fila.TransactionId,
        fila.CausationId,
        fila.AggregateType,
        fila.AggregateId,
        fila.Source,
        JsonDocument.Parse(fila.Payload).RootElement.Clone(),
        fila.FechaRecepcion);
}