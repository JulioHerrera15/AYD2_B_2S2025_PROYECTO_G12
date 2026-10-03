using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;

namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Agregado raiz del contexto Pagos. Hay un Pago por orden. Acumula los intentos
/// de cobro contra el proveedor y lleva su propia <see cref="Secuencia" />
/// monotonica para el campo sequence del envelope de eventos.
/// </summary>
public sealed class Pago
{
    private readonly List<IntentoPago> _intentos = new();

    private Pago()
    {
        OrdenId = string.Empty;
        ClienteId = string.Empty;
    }

    public Pago(
        string ordenId,
        string clienteId,
        Canal canal,
        Moneda moneda,
        decimal monto,
        MedioPago medioPago,
        DateTime? creadoEn = null,
        ConceptoPago concepto = ConceptoPago.ORDEN)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(ordenId));
        }

        if (string.IsNullOrWhiteSpace(clienteId))
        {
            throw new ArgumentException("El identificador del cliente es obligatorio.", nameof(clienteId));
        }

        if (monto <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto del pago debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        OrdenId = ordenId;
        ClienteId = clienteId;
        Canal = canal;
        Moneda = moneda;
        Monto = monto;
        MedioPago = medioPago;
        Concepto = concepto;
        Estado = EstadoPago.INICIADO;
        Secuencia = 0;
        CreadoEn = (creadoEn ?? DateTime.UtcNow).ToUniversalTime();
        ActualizadoEn = CreadoEn;
    }

    public Guid Id { get; private set; }

    public string OrdenId { get; private set; }

    public string ClienteId { get; private set; }

    public Canal Canal { get; private set; }

    public Moneda Moneda { get; private set; }

    public decimal Monto { get; private set; }

    public MedioPago MedioPago { get; private set; }

    /// <summary>ORDEN (cobro de una orden) o ABONO_CREDITO (OrdenId lleva la referencia del abono).</summary>
    public ConceptoPago Concepto { get; private set; }

    public EstadoPago Estado { get; private set; }

    /// <summary>
    /// Secuencia monotonica por orden. Arranca en 0 y se incrementa con cada
    /// evento de resultado que emite el pago. El primer resultado va con sequence 1.
    /// </summary>
    public int Secuencia { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ActualizadoEn { get; private set; }

    public IReadOnlyList<IntentoPago> Intentos => _intentos.AsReadOnly();

    public int SiguienteNumeroIntento => _intentos.Count + 1;

    /// <summary>Registra un intento nuevo en estado INICIADO. Devuelve el intento para completarlo luego.</summary>
    public IntentoPago RegistrarIntento(
        MedioPago medioPago,
        string? tokenMedioPago,
        string? ultimosCuatro,
        string? marca,
        DateTime? iniciadoEn = null)
    {
        var intento = new IntentoPago(
            SiguienteNumeroIntento,
            medioPago,
            tokenMedioPago,
            ultimosCuatro,
            marca,
            Monto,
            Moneda,
            (iniciadoEn ?? DateTime.UtcNow).ToUniversalTime());

        _intentos.Add(intento);
        return intento;
    }

    public void MarcarAplicado(DateTime? cuando = null) => CambiarEstado(EstadoPago.APLICADO, cuando);

    public void MarcarRechazado(DateTime? cuando = null) => CambiarEstado(EstadoPago.RECHAZADO, cuando);

    public void MarcarPendiente(DateTime? cuando = null) => CambiarEstado(EstadoPago.PENDIENTE, cuando);

    /// <summary>Genera un enlace de reintento con token opaco y la validez indicada.</summary>
    public EnlaceReintento GenerarEnlaceReintento(TimeSpan validez, DateTime? ahora = null)
    {
        var momento = (ahora ?? DateTime.UtcNow).ToUniversalTime();
        return new EnlaceReintento(OrdenId, momento.Add(validez), momento);
    }

    private void CambiarEstado(EstadoPago nuevo, DateTime? cuando)
    {
        Estado = nuevo;
        Secuencia += 1;
        ActualizadoEn = (cuando ?? DateTime.UtcNow).ToUniversalTime();
    }
}
