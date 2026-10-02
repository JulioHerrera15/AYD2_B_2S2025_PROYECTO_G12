namespace SgoLogic.Credito.Api.Domain;

/// <summary>Estado de una cuenta por cobrar. PAGADA y VENCIDA quedan para el bloque de pagos.</summary>
public enum EstadoCuentaPorCobrar
{
    VIGENTE,
    PAGADA,
    VENCIDA,
    ANULADA
}

/// <summary>
/// Cargo de una compra a credito: lo que el cliente debe pagar por una orden, con
/// fecha limite. Hay a lo sumo una por orden. Su Id es el cargoId de los eventos.
/// </summary>
public sealed class CuentaPorCobrar
{
    public const int LargoMaximoOrdenId = 40;
    public const int LargoMaximoMotivo = 200;

    private CuentaPorCobrar()
    {
    }

    public CuentaPorCobrar(
        string ordenId,
        string numeroOrden,
        string clienteId,
        decimal monto,
        string moneda,
        DateTime emitidaEn,
        int plazoDias)
    {
        if (monto <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto del cargo debe ser mayor que 0.");
        }

        Id = Guid.NewGuid();
        OrdenId = ordenId;
        NumeroOrden = numeroOrden;
        ClienteId = clienteId;
        MontoOriginal = monto;
        SaldoPendiente = monto;
        Moneda = moneda;
        EmitidaEn = emitidaEn;
        VenceEn = emitidaEn.AddDays(plazoDias);
        Estado = EstadoCuentaPorCobrar.VIGENTE;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string OrdenId { get; private set; } = string.Empty;
    public string NumeroOrden { get; private set; } = string.Empty;
    public string ClienteId { get; private set; } = string.Empty;
    public decimal MontoOriginal { get; private set; }
    public decimal SaldoPendiente { get; private set; }
    public string Moneda { get; private set; } = string.Empty;
    public DateTime EmitidaEn { get; private set; }
    public DateTime VenceEn { get; private set; }
    public EstadoCuentaPorCobrar Estado { get; private set; }
    public DateTime? AnuladaEn { get; private set; }
    public string? MotivoAnulacion { get; private set; }

    /// <summary>Concurrencia optimista.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Anula una cuenta VIGENTE y devuelve el saldo que hay que liberar en la linea.
    /// Devuelve 0 si ya no estaba vigente (anular dos veces no libera dos veces).
    /// </summary>
    public decimal Anular(string motivo, DateTime ahora)
    {
        if (Estado != EstadoCuentaPorCobrar.VIGENTE)
        {
            return 0m;
        }

        var liberado = SaldoPendiente;
        Estado = EstadoCuentaPorCobrar.ANULADA;
        SaldoPendiente = 0m;
        AnuladaEn = ahora;
        MotivoAnulacion = motivo.Length > LargoMaximoMotivo ? motivo[..LargoMaximoMotivo] : motivo;
        Version++;
        return liberado;
    }
}
