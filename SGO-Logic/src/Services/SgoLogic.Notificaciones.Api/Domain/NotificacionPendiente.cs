namespace SgoLogic.Notificaciones.Api.Domain;

public enum EstadoNotificacionPendiente
{
    /// <summary>Esperando su proximo reintento.</summary>
    PENDIENTE,

    /// <summary>Un reintento la entrego.</summary>
    ENVIADA,

    /// <summary>Se agotaron los reintentos sin entregarla.</summary>
    FALLIDA
}

/// <summary>
/// Notificacion que no se pudo entregar al proveedor de mensajeria (B3). Antes se
/// perdia: el consumidor hacia ack aunque el envio fallara. Ahora se guarda aqui, en
/// la misma transaccion que la marca de mensaje procesado, y un worker la reintenta
/// con backoff exponencial: el primer reintento a los 10 s del fallo y los siguientes
/// a 20, 40, 80, 160 y 320 s del anterior. Tras <see cref="MaximoIntentos" /> reintentos
/// fallidos queda FALLIDA.
///
/// <see cref="EventId" /> es el evento que la origino (unico): la deduplicacion por
/// eventId del consumidor sigue siendo la que evita notificar dos veces.
/// </summary>
public sealed class NotificacionPendiente
{
    public const int MaximoIntentos = 6;
    public static readonly TimeSpan PrimerReintento = TimeSpan.FromSeconds(10);
    public const int LargoMaximoError = 500;

    private NotificacionPendiente()
    {
        EventId = string.Empty;
        Canal = string.Empty;
        Destinatario = string.Empty;
        Asunto = string.Empty;
        Cuerpo = string.Empty;
    }

    public NotificacionPendiente(
        string eventId,
        string canal,
        string destinatario,
        string asunto,
        string cuerpo,
        string? error,
        DateTime ahora)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("El evento de origen es obligatorio.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(destinatario))
        {
            throw new ArgumentException("El destinatario es obligatorio.", nameof(destinatario));
        }

        Id = Guid.NewGuid();
        EventId = eventId;
        Canal = canal;
        Destinatario = destinatario.Trim();
        Asunto = asunto;
        Cuerpo = cuerpo;
        Estado = EstadoNotificacionPendiente.PENDIENTE;
        Intentos = 0;
        UltimoError = Recortar(error);
        CreadaEn = ahora.ToUniversalTime();
        ActualizadaEn = CreadaEn;
        ProximoIntentoEn = CreadaEn + PrimerReintento;
    }

    public Guid Id { get; private set; }

    public string EventId { get; private set; }

    public string Canal { get; private set; }

    public string Destinatario { get; private set; }

    public string Asunto { get; private set; }

    public string Cuerpo { get; private set; }

    public EstadoNotificacionPendiente Estado { get; private set; }

    /// <summary>Reintentos ya hechos (el envio original fallido no cuenta).</summary>
    public int Intentos { get; private set; }

    public DateTime ProximoIntentoEn { get; private set; }

    public string? UltimoError { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public DateTime ActualizadaEn { get; private set; }

    public DateTime? EnviadaEn { get; private set; }

    public bool TocaReintentar(DateTime ahora) =>
        Estado == EstadoNotificacionPendiente.PENDIENTE && ProximoIntentoEn <= ahora.ToUniversalTime();

    public void MarcarEnviada(DateTime ahora)
    {
        Intentos++;
        Estado = EstadoNotificacionPendiente.ENVIADA;
        EnviadaEn = ahora.ToUniversalTime();
        ActualizadaEn = EnviadaEn.Value;
        UltimoError = null;
    }

    /// <summary>
    /// Un reintento fallo: agenda el siguiente con backoff exponencial (10 s por 2 a la
    /// n) o, si ya van <see cref="MaximoIntentos" />, la deja FALLIDA.
    /// </summary>
    public void RegistrarFallo(string? error, DateTime ahora)
    {
        var momento = ahora.ToUniversalTime();
        Intentos++;
        UltimoError = Recortar(error);
        ActualizadaEn = momento;

        if (Intentos >= MaximoIntentos)
        {
            Estado = EstadoNotificacionPendiente.FALLIDA;
            return;
        }

        ProximoIntentoEn = momento + TimeSpan.FromTicks(PrimerReintento.Ticks * (1L << Intentos));
    }

    private static string? Recortar(string? error) =>
        string.IsNullOrWhiteSpace(error) ? null : error.Length > LargoMaximoError ? error[..LargoMaximoError] : error;
}
