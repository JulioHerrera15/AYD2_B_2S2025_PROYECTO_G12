using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Un intento de cobro contra el proveedor. Hijo de <see cref="Pago" />. Se
/// registra en estado INICIADO ANTES de invocar a PayKlik y nunca se borra: el
/// criterio de HU-004 exige que el intento quede registrado aunque el proveedor no
/// responda.
///
/// Restriccion dura (RES-08): no guarda ni guardara numero de tarjeta, CVV ni
/// fecha de vencimiento. Solo token del proveedor, ultimos cuatro y marca.
/// </summary>
public sealed class IntentoPago
{
    private IntentoPago()
    {
    }

    public IntentoPago(
        int intentoNumero,
        MedioPago medioPago,
        string? tokenMedioPago,
        string? ultimosCuatro,
        string? marca,
        decimal monto,
        Moneda moneda,
        DateTime iniciadoEn)
    {
        if (intentoNumero <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intentoNumero), "El numero de intento debe ser mayor que cero.");
        }

        if (monto <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto del intento debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        IntentoNumero = intentoNumero;
        MedioPago = medioPago;
        TokenMedioPago = tokenMedioPago;
        UltimosCuatro = ultimosCuatro;
        Marca = marca;
        Monto = monto;
        Moneda = moneda;
        Estado = EstadoIntentoPago.INICIADO;
        IniciadoEn = iniciadoEn;
    }

    public Guid Id { get; private set; }

    public int IntentoNumero { get; private set; }

    public MedioPago MedioPago { get; private set; }

    public string? TokenMedioPago { get; private set; }

    public string? UltimosCuatro { get; private set; }

    public string? Marca { get; private set; }

    public decimal Monto { get; private set; }

    public Moneda Moneda { get; private set; }

    public EstadoIntentoPago Estado { get; private set; }

    public string? CodigoRespuesta { get; private set; }

    public string? MensajeRespuesta { get; private set; }

    public int? LatenciaMs { get; private set; }

    /// <summary>
    /// Identificador de autorizacion devuelto por el proveedor en un intento
    /// exitoso. Se persiste para poder reconstruir el evento PagoAplicado y para
    /// auditoria. No es dato de tarjeta.
    /// </summary>
    public string? AutorizacionId { get; private set; }

    public DateTime IniciadoEn { get; private set; }

    public DateTime? FinalizadoEn { get; private set; }

    public void MarcarExitoso(
        string autorizacionId,
        string codigoRespuesta,
        string mensajeRespuesta,
        string? ultimosCuatro,
        string? marca,
        int latenciaMs,
        DateTime? finalizadoEn = null)
    {
        Estado = EstadoIntentoPago.EXITOSO;
        AutorizacionId = autorizacionId;
        CodigoRespuesta = codigoRespuesta;
        MensajeRespuesta = mensajeRespuesta;
        UltimosCuatro = ultimosCuatro ?? UltimosCuatro;
        Marca = marca ?? Marca;
        LatenciaMs = latenciaMs;
        FinalizadoEn = (finalizadoEn ?? DateTime.UtcNow).ToUniversalTime();
    }

    public void MarcarRechazado(
        string codigoRespuesta,
        string mensajeRespuesta,
        int latenciaMs,
        DateTime? finalizadoEn = null)
    {
        Estado = EstadoIntentoPago.RECHAZADO;
        CodigoRespuesta = codigoRespuesta;
        MensajeRespuesta = mensajeRespuesta;
        LatenciaMs = latenciaMs;
        FinalizadoEn = (finalizadoEn ?? DateTime.UtcNow).ToUniversalTime();
    }

    public void MarcarFallido(
        string codigoRespuesta,
        string motivoFalla,
        int latenciaMs,
        DateTime? finalizadoEn = null)
    {
        Estado = EstadoIntentoPago.FALLIDO;
        CodigoRespuesta = codigoRespuesta;
        MensajeRespuesta = motivoFalla;
        LatenciaMs = latenciaMs;
        FinalizadoEn = (finalizadoEn ?? DateTime.UtcNow).ToUniversalTime();
    }
}
