namespace SgoLogic.Credito.Api.Domain;

/// <summary>Tipo de un <see cref="MovimientoCuenta" />.</summary>
public enum TipoMovimientoCuenta
{
    /// <summary>Devolucion de lo comprado. Referencia = devolucionId; unico por devolucion.</summary>
    DEVOLUCION,

    /// <summary>Pagos confirmo el reembolso de (parte de) un abono. Referencia = reembolsoId.</summary>
    REEMBOLSO_ABONO
}

/// <summary>
/// Movimiento de una cuenta por cobrar que no es un abono. El par (Tipo, Referencia) es
/// unico: es lo que hace idempotente aplicar una devolucion (por devolucionId) o la
/// confirmacion de un reembolso (por reembolsoId).
/// </summary>
public sealed class MovimientoCuenta
{
    public const int LargoMaximoReferencia = 64;

    private MovimientoCuenta()
    {
    }

    public MovimientoCuenta(
        Guid cuentaId,
        string clienteId,
        TipoMovimientoCuenta tipo,
        decimal monto,
        string referencia,
        DateTime fecha,
        decimal montoDescontadoDeuda = 0m,
        decimal montoAReembolsar = 0m,
        Guid? abonoId = null)
    {
        if (monto <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto del movimiento debe ser mayor que 0.");
        }

        if (string.IsNullOrWhiteSpace(referencia) || referencia.Length > LargoMaximoReferencia)
        {
            throw new ArgumentException("La referencia del movimiento es obligatoria y corta.", nameof(referencia));
        }

        Id = Guid.NewGuid();
        CuentaId = cuentaId;
        ClienteId = clienteId;
        Tipo = tipo;
        Monto = monto;
        Referencia = referencia;
        Fecha = fecha;
        MontoDescontadoDeuda = montoDescontadoDeuda;
        MontoAReembolsar = montoAReembolsar;
        AbonoId = abonoId;
    }

    public Guid Id { get; private set; }
    public Guid CuentaId { get; private set; }
    public string ClienteId { get; private set; } = string.Empty;
    public TipoMovimientoCuenta Tipo { get; private set; }

    /// <summary>DEVOLUCION: monto devuelto. REEMBOLSO_ABONO: monto reembolsado a la tarjeta.</summary>
    public decimal Monto { get; private set; }

    public string Referencia { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }

    /// <summary>Solo DEVOLUCION: parte que bajo la deuda (y volvio al disponible).</summary>
    public decimal MontoDescontadoDeuda { get; private set; }

    /// <summary>Solo DEVOLUCION: parte ya pagada que se pidio reembolsar a la tarjeta.</summary>
    public decimal MontoAReembolsar { get; private set; }

    /// <summary>Solo REEMBOLSO_ABONO: abono reembolsado.</summary>
    public Guid? AbonoId { get; private set; }
}
