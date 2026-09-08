namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Fila de la tabla outbox propia del contexto Ordenes. Se escribe en la misma
/// transaccion que el cambio de estado de la orden; un publicador por polling la
/// toma despues y la marca como publicada o fallida. Garantiza el "exactamente una
/// vez a efectos de negocio" del contrato de eventos.
/// </summary>
public sealed class MensajeSaliente
{
    private MensajeSaliente()
    {
        TipoEvento = string.Empty;
        AgregadoId = string.Empty;
        Payload = string.Empty;
    }

    public MensajeSaliente(
        string tipoEvento,
        string agregadoId,
        int secuencia,
        string payload,
        DateTime? creadoEn = null)
    {
        if (string.IsNullOrWhiteSpace(tipoEvento))
        {
            throw new ArgumentException("El tipo de evento es obligatorio.", nameof(tipoEvento));
        }

        if (string.IsNullOrWhiteSpace(agregadoId))
        {
            throw new ArgumentException("El identificador del agregado es obligatorio.", nameof(agregadoId));
        }

        if (secuencia <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secuencia), "La secuencia debe ser mayor que cero.");
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("El payload del evento es obligatorio.", nameof(payload));
        }

        Id = Guid.NewGuid();
        TipoEvento = tipoEvento;
        AgregadoId = agregadoId;
        Secuencia = secuencia;
        Payload = payload;
        Estado = EstadoMensajeSaliente.Pendiente;
        Intentos = 0;
        CreadoEn = (creadoEn ?? DateTime.UtcNow).ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public string TipoEvento { get; private set; }

    /// <summary>
    /// Identificador del agregado que origino el evento. Coincide siempre con
    /// <see cref="Orden.Id" />. Es texto y no Guid porque el id de la orden es
    /// texto en el dominio.
    /// </summary>
    public string AgregadoId { get; private set; }

    public int Secuencia { get; private set; }

    public string Payload { get; private set; }

    public EstadoMensajeSaliente Estado { get; private set; }

    public int Intentos { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime? PublicadoEn { get; private set; }

    public string? UltimoError { get; private set; }

    /// <summary>Marca el mensaje como publicado por el publicador de outbox.</summary>
    public void MarcarPublicado(DateTime? publicadoEn = null)
    {
        Estado = EstadoMensajeSaliente.Publicado;
        PublicadoEn = (publicadoEn ?? DateTime.UtcNow).ToUniversalTime();
        UltimoError = null;
    }

    /// <summary>Registra un intento fallido de publicacion.</summary>
    public void MarcarFallido(string error)
    {
        Estado = EstadoMensajeSaliente.Fallido;
        Intentos += 1;
        UltimoError = error;
    }
}
