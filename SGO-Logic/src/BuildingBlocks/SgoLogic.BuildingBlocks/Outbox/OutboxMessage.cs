namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>
/// Fila de la tabla outbox de un contexto. Vive en la base de ese contexto,
/// nunca centralizada. El envelope completo ya serializado va en Payload: el
/// publicador no reconstruye nada, solo lo toma y lo publica tal cual.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(
        string eventId,
        string eventType,
        string payload,
        string transactionId,
        string exchange,
        string routingKey,
        string aggregateType,
        string aggregateId)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        Payload = payload;
        TransactionId = transactionId;
        Exchange = exchange;
        RoutingKey = routingKey;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        FechaCreacion = DateTime.UtcNow;
        IntentosFallidos = 0;
        Estado = EstadoOutbox.Pendiente;
    }

    public Guid Id { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string TransactionId { get; private set; } = string.Empty;
    public string Exchange { get; private set; } = string.Empty;
    public string RoutingKey { get; private set; } = string.Empty;
    public string AggregateType { get; private set; } = string.Empty;
    public string AggregateId { get; private set; } = string.Empty;
    public DateTime FechaCreacion { get; private set; }
    public DateTime? FechaPublicacion { get; private set; }
    public int IntentosFallidos { get; private set; }
    public DateTime? ProximoIntentoEn { get; private set; }
    public EstadoOutbox Estado { get; private set; }

    public void MarcarPublicado(DateTime momento)
    {
        Estado = EstadoOutbox.Publicado;
        FechaPublicacion = momento;
        ProximoIntentoEn = null;
    }

    /// <summary>
    /// Backoff exponencial con techo de 5 minutos. Al llegar a maxReintentos la
    /// fila pasa a Fallido: quien la procesa (OutboxPublisher) es responsable de
    /// enviarla entonces a sgo.eventos.dlx.
    /// </summary>
    public void RegistrarFallo(int maxReintentos)
    {
        IntentosFallidos += 1;

        if (IntentosFallidos >= maxReintentos)
        {
            Estado = EstadoOutbox.Fallido;
            ProximoIntentoEn = null;
            return;
        }

        var segundosBackoff = Math.Min(Math.Pow(2, IntentosFallidos), 300);
        ProximoIntentoEn = DateTime.UtcNow.AddSeconds(segundosBackoff);
    }
}