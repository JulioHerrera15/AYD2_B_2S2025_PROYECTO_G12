using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>
/// Implementacion generica de IOutboxWriter, atada al TDbContext del servicio que
/// la registra. Se resuelve con la misma vida (scoped) que ese DbContext, asi que
/// comparten la misma instancia dentro de una solicitud o de un mensaje procesado:
/// por eso Encolar + el SaveChangesAsync que hace la aplicacion quedan en la misma
/// transaccion.
/// </summary>
internal sealed class OutboxWriter<TDbContext> : IOutboxWriter
    where TDbContext : DbContext
{
    private readonly TDbContext _contexto;
    private readonly OutboxOptions _opciones;

    public OutboxWriter(TDbContext contexto, IOptions<OutboxOptions> opciones)
    {
        _contexto = contexto;
        _opciones = opciones.Value;
    }

    public void Encolar<TData>(
        string eventType,
        string aggregateType,
        string aggregateId,
        int sequence,
        string exchange,
        string routingKey,
        TData data)
        where TData : notnull
    {
        var transactionId = EventContext.TransactionId ?? Guid.NewGuid().ToString();
        var causationId = EventContext.CausationId;

        var envelope = new IntegrationEvent<TData>
        {
            EventType = eventType,
            Sequence = sequence,
            TransactionId = transactionId,
            CausationId = causationId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Source = _opciones.Source,
            Data = data
        };

        var payload = JsonSerializer.Serialize(envelope, SgoJsonSerializerOptions.Default);

        var mensaje = new OutboxMessage(
            envelope.EventId,
            eventType,
            payload,
            transactionId,
            exchange,
            routingKey,
            aggregateType,
            aggregateId);

        _contexto.Set<OutboxMessage>().Add(mensaje);
    }
}