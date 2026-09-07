namespace SgoLogic.Contracts;

/// <summary>
/// Interfaz marcadora que expone los campos comunes del envelope de eventos de
/// integracion, independientemente del tipo de dato que transporte cada evento.
/// </summary>
public interface IIntegrationEvent
{
    string EventId { get; }
    string EventType { get; }
    int EventVersion { get; }
    DateTime OccurredAt { get; }
    int Sequence { get; }
    string TransactionId { get; }
    string? CausationId { get; }
    string AggregateType { get; }
    string AggregateId { get; }
    string Source { get; }
}
