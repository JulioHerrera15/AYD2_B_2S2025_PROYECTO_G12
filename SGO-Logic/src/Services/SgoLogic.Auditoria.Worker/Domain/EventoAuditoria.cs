namespace SgoLogic.Auditoria.Worker.Domain;

/// <summary>
/// Copia inmutable de un envelope de integracion recibido desde sgo.eventos. No
/// tiene ningun metodo de modificacion a proposito: una vez insertada, esta fila
/// no cambia. La inmutabilidad real la garantiza el motor (permisos del rol de la
/// app + trigger BEFORE UPDATE OR DELETE, ver PermisosAuditoriaAplicador); esto
/// solo evita que el propio codigo de aplicacion tenga como escribir un UPDATE.
/// </summary>
public sealed class EventoAuditoria
{
    private EventoAuditoria()
    {
    }

    public EventoAuditoria(
        string eventId,
        string eventType,
        int eventVersion,
        DateTime occurredAt,
        int sequence,
        string transactionId,
        string? causationId,
        string aggregateType,
        string aggregateId,
        string source,
        string payload,
        DateTime fechaRecepcion)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        EventVersion = eventVersion;
        OccurredAt = occurredAt;
        Sequence = sequence;
        TransactionId = transactionId;
        CausationId = causationId;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        Source = source;
        Payload = payload;
        FechaRecepcion = fechaRecepcion;
    }

    public Guid Id { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public int Sequence { get; private set; }
    public string TransactionId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }
    public string AggregateType { get; private set; } = string.Empty;
    public string AggregateId { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;

    /// <summary>Data cruda del envelope (evento.Data.GetRawText()), sin normalizar ni transformar.</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Momento en que Auditoria persistio el evento, distinto de OccurredAt (cuando el origen lo genero).</summary>
    public DateTime FechaRecepcion { get; private set; }
}