using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.Credito.Tests.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IOutboxWriter" />. Acumula cada evento que la
/// aplicacion encola, sin base de datos ni broker.
/// </summary>
public sealed class OutboxWriterFalso : IOutboxWriter
{
    public sealed record EventoEncolado(
        string EventType,
        string AggregateType,
        string AggregateId,
        int Sequence,
        string Exchange,
        string RoutingKey,
        object Data);

    public List<EventoEncolado> Eventos { get; } = new();

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
        Eventos.Add(new EventoEncolado(eventType, aggregateType, aggregateId, sequence, exchange, routingKey, data));
    }
}
