namespace SgoLogic.Contracts;

/// <summary>
/// Envelope congelado de eventos de integracion. transactionId correlaciona la saga
/// completa; el identificador del agregado (por ejemplo, el id de una orden) va en
/// aggregateId, nunca en transactionId.
/// </summary>
public sealed class IntegrationEvent<TData> : IIntegrationEvent
    where TData : notnull
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();

    public required string EventType { get; init; }

    public int EventVersion { get; init; } = 1;

    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public required int Sequence { get; init; }

    public required string TransactionId { get; init; }

    public string? CausationId { get; init; }

    public required string AggregateType { get; init; }

    public required string AggregateId { get; init; }

    public required string Source { get; init; }

    public required TData Data { get; init; }
}
