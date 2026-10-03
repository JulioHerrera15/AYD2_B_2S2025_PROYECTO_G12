namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Estado de una cuenta por cobrar. VIGENTE y VENCIDA tienen saldo y se pueden pagar;
/// PAGADA y ANULADA son finales.
/// </summary>
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

    /// <summary>Momento en que el saldo llego a 0. Null si no esta PAGADA.</summary>
    public DateTime? PagadaEn { get; private set; }

    /// <summary>True si se pago a mas tardar en VenceEn. Null si no esta PAGADA.</summary>
    public bool? PagoPuntual { get; private set; }

    /// <summary>Cuando se emitio el recordatorio de "por vencer"; se emite una sola vez.</summary>
    public DateTime? RecordatorioEnviadoEn { get; private set; }

    /// <summary>Concurrencia optimista.</summary>
    public int Version { get; private set; }

    /// <summary>VIGENTE o VENCIDA con saldo: el cliente la puede pagar.</summary>
    public bool EsPagable =>
        Estado is EstadoCuentaPorCobrar.VIGENTE or EstadoCuentaPorCobrar.VENCIDA && SaldoPendiente > 0m;

    /// <summary>
    /// Resta un abono cobrado del saldo. Si llega a 0 la cuenta queda PAGADA, con
    /// PagoPuntual = (ahora &lt;= VenceEn). Devuelve true si quedo pagada.
    /// </summary>
    public bool AplicarAbono(decimal monto, DateTime ahora)
    {
        if (!EsPagable)
        {
            throw new InvalidOperationException("La cuenta no admite abonos en su estado actual.");
        }

        if (monto <= 0m || monto > SaldoPendiente)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El abono debe ser mayor que 0 y no mayor que el saldo.");
        }

        SaldoPendiente -= monto;
        Version++;

        if (SaldoPendiente > 0m)
        {
            return false;
        }

        Estado = EstadoCuentaPorCobrar.PAGADA;
        PagadaEn = ahora;
        PagoPuntual = ahora <= VenceEn;
        return true;
    }

    /// <summary>VIGENTE con saldo y VenceEn ya pasado: pasa a VENCIDA. Devuelve false si no aplica.</summary>
    public bool Vencer(DateTime ahora)
    {
        if (Estado != EstadoCuentaPorCobrar.VIGENTE || SaldoPendiente <= 0m || VenceEn >= ahora)
        {
            return false;
        }

        Estado = EstadoCuentaPorCobrar.VENCIDA;
        Version++;
        return true;
    }

    /// <summary>Marca el recordatorio de "por vencer". Devuelve false si ya se habia enviado.</summary>
    public bool MarcarRecordatorio(DateTime ahora)
    {
        if (RecordatorioEnviadoEn is not null)
        {
            return false;
        }

        RecordatorioEnviadoEn = ahora;
        Version++;
        return true;
    }

    /// <summary>Solo para demo: mueve la fecha de vencimiento y permite un recordatorio nuevo.</summary>
    public void FijarVencimiento(DateTime venceEn)
    {
        VenceEn = venceEn;
        RecordatorioEnviadoEn = null;
        Version++;
    }

    /// <summary>
    /// Anula una cuenta con saldo (VIGENTE o VENCIDA) y devuelve el saldo pendiente
    /// que hay que liberar en la linea; lo ya abonado no se toca (reembolso, bloque 4).
    /// Devuelve 0 si ya estaba PAGADA o ANULADA (anular dos veces no libera dos veces).
    /// </summary>
    public decimal Anular(string motivo, DateTime ahora)
    {
        if (Estado is not (EstadoCuentaPorCobrar.VIGENTE or EstadoCuentaPorCobrar.VENCIDA))
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
