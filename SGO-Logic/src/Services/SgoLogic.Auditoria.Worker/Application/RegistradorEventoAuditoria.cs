using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.Auditoria.Worker.Domain;
using SgoLogic.Auditoria.Worker.Infrastructure;
using SgoLogic.Contracts;

namespace SgoLogic.Auditoria.Worker.Application;

/// <summary>
/// Logica de insercion, separada de AuditoriaConsumidor para poder probarla sin
/// un RabbitMQ real: recibe el envelope ya deserializado y el DbContext del
/// mensaje en curso, y solo agrega la fila (nunca llama a SaveChangesAsync, eso
/// lo hace EventConsumerBase al volver de ManejarAsync, junto con la marca de
/// mensajes_procesados, en un solo SaveChanges).
///
/// TData es JsonElement a proposito: acepta cualquier forma de "data" sin
/// conocer el tipo concreto, asi Auditoria no se rompe cuando otro contexto
/// agrega un evento nuevo que no existe en Contracts.
/// </summary>
public sealed class RegistradorEventoAuditoria
{
    public async Task RegistrarAsync(
        IntegrationEvent<JsonElement> evento,
        AuditoriaDbContext contexto,
        CancellationToken cancelacion)
    {
        var yaExiste = await contexto.EventosAuditoria
            .AnyAsync(e => e.EventId == evento.EventId, cancelacion);

        if (yaExiste)
        {
            MetricasAuditoria.EventosDescartadosTotal.Inc();
            return;
        }

        var fila = new EventoAuditoria(
            evento.EventId,
            evento.EventType,
            evento.EventVersion,
            evento.OccurredAt,
            evento.Sequence,
            evento.TransactionId,
            evento.CausationId,
            evento.AggregateType,
            evento.AggregateId,
            evento.Source,
            evento.Data.GetRawText(),
            DateTime.UtcNow);

        contexto.EventosAuditoria.Add(fila);

        MetricasAuditoria.EventosRegistradosTotal.WithLabels(evento.Source).Inc();
    }
}