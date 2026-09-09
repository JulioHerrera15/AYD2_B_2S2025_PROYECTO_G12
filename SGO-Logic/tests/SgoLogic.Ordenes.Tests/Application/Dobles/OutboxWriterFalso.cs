using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IOutboxWriter" />. Acumula cada evento que la
/// aplicacion encola, sin base de datos ni broker. Permite verificar tipo de
/// evento, routing key, agregado, secuencia y contenido de <c>data</c>.
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

    public IEnumerable<EventoEncolado> PorTipo(string eventType) =>
        Eventos.Where(evento => evento.EventType == eventType);

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
        Eventos.Add(new EventoEncolado(
            eventType, aggregateType, aggregateId, sequence, exchange, routingKey, data));
    }
}
