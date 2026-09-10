namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Marca de un mensaje de integracion ya aplicado por el contexto Ordenes. El
/// consumidor de eventos de Pagos la consulta antes de tocar la saga: si el
/// <see cref="MessageId" /> ya existe, hace ack y no aplica nada. La insercion de
/// esta marca va en la misma transaccion que la transicion de la orden y la
/// escritura al outbox (envelope, regla 2: consumo idempotente).
/// </summary>
public sealed class MensajeProcesado
{
    private MensajeProcesado()
    {
        MessageId = string.Empty;
        TipoEvento = string.Empty;
    }

    public MensajeProcesado(string messageId, string tipoEvento, DateTime fecha)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("El messageId es obligatorio.", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(tipoEvento))
        {
            throw new ArgumentException("El tipo de evento es obligatorio.", nameof(tipoEvento));
        }

        Id = Guid.NewGuid();
        MessageId = messageId;
        TipoEvento = tipoEvento;
        Fecha = fecha;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Identificador irrepetible del evento consumido (campo <c>eventId</c> del
    /// sobre). Es el ancla de idempotencia y tiene indice unico.
    /// </summary>
    public string MessageId { get; private set; }

    /// <summary>Nombre del evento aplicado (campo <c>eventType</c> del sobre).</summary>
    public string TipoEvento { get; private set; }

    /// <summary>Momento en que se aplico el mensaje, en UTC.</summary>
    public DateTime Fecha { get; private set; }
}
