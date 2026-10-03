namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Estado de un abono. SOLICITADO y PENDIENTE estan "en curso" (solo uno por
/// cuenta); APLICADO, RECHAZADO y REEMBOLSO_PENDIENTE son finales.
/// </summary>
public enum EstadoAbonoCredito
{
    SOLICITADO,

    /// <summary>El cobro quedo sin resultado cierto; se puede reintentar con el mismo abono.</summary>
    PENDIENTE,

    APLICADO,
    RECHAZADO,

    /// <summary>Se cobro, pero la cuenta ya estaba anulada: el reembolso es del bloque 4.</summary>
    REEMBOLSO_PENDIENTE
}

/// <summary>
/// Pago (total o parcial) de una cuenta por cobrar con tarjeta. El cobro lo hace
/// Pagos al consumir CobroAbonoCreditoSolicitado; Credito nunca llama al proveedor.
/// Un reintento reutiliza el mismo abono (mismo Id), asi que nunca hay cobro doble.
/// </summary>
public sealed class AbonoCredito
{
    public const int LargoMaximoCodigo = 60;

    private AbonoCredito()
    {
    }

    public AbonoCredito(string referencia, CuentaPorCobrar cuenta, decimal monto, DateTime ahora)
    {
        Id = Guid.NewGuid();
        Referencia = referencia;
        CuentaId = cuenta.Id;
        ClienteId = cuenta.ClienteId;
        Monto = monto;
        Moneda = cuenta.Moneda;
        Estado = EstadoAbonoCredito.SOLICITADO;
        IntentoNumero = 1;
        SolicitadoEn = ahora;
        Version = 1;
    }

    public Guid Id { get; private set; }

    /// <summary>ABO-{yyyy}-{correlativo de 6 digitos}; unica.</summary>
    public string Referencia { get; private set; } = string.Empty;

    public Guid CuentaId { get; private set; }
    public string ClienteId { get; private set; } = string.Empty;
    public decimal Monto { get; private set; }
    public string Moneda { get; private set; } = string.Empty;
    public EstadoAbonoCredito Estado { get; private set; }
    public int IntentoNumero { get; private set; }
    public string? CodigoRespuesta { get; private set; }
    public string? AutorizacionId { get; private set; }
    public string? UltimosCuatro { get; private set; }
    public string? Marca { get; private set; }
    public DateTime SolicitadoEn { get; private set; }
    public DateTime? ResueltoEn { get; private set; }

    /// <summary>Concurrencia optimista.</summary>
    public int Version { get; private set; }

    public bool EnCurso => Estado is EstadoAbonoCredito.SOLICITADO or EstadoAbonoCredito.PENDIENTE;

    public bool EsFinal => !EnCurso;

    public bool PuedeReintentar => Estado == EstadoAbonoCredito.PENDIENTE;

    /// <summary>Nuevo intento del mismo abono. Solo desde PENDIENTE.</summary>
    public void Reintentar(DateTime ahora)
    {
        if (!PuedeReintentar)
        {
            throw new InvalidOperationException("Solo un abono pendiente se puede reintentar.");
        }

        IntentoNumero++;
        Estado = EstadoAbonoCredito.SOLICITADO;
        CodigoRespuesta = null;
        SolicitadoEn = ahora;
        Version++;
    }

    public void MarcarPendiente(string? motivo, DateTime ahora)
    {
        Estado = EstadoAbonoCredito.PENDIENTE;
        CodigoRespuesta = Recortar(motivo);
        ResueltoEn = ahora;
        Version++;
    }

    public void MarcarRechazado(string codigo, DateTime ahora)
    {
        Estado = EstadoAbonoCredito.RECHAZADO;
        CodigoRespuesta = Recortar(codigo);
        ResueltoEn = ahora;
        Version++;
    }

    public void MarcarAplicado(string autorizacionId, string? ultimosCuatro, string? marca, DateTime ahora)
    {
        Estado = EstadoAbonoCredito.APLICADO;
        RegistrarTarjeta(autorizacionId, ultimosCuatro, marca);
        ResueltoEn = ahora;
        Version++;
    }

    /// <summary>
    /// El dinero se cobro pero la cuenta ya no lo admite (anulada), o una cuenta con
    /// este abono aplicado se anulo. Queda para el reembolso del bloque 4.
    /// </summary>
    public void MarcarReembolsoPendiente(string? autorizacionId, string? ultimosCuatro, string? marca, DateTime ahora)
    {
        Estado = EstadoAbonoCredito.REEMBOLSO_PENDIENTE;
        if (!string.IsNullOrWhiteSpace(autorizacionId))
        {
            RegistrarTarjeta(autorizacionId, ultimosCuatro, marca);
        }

        ResueltoEn ??= ahora;
        Version++;
    }

    private void RegistrarTarjeta(string autorizacionId, string? ultimosCuatro, string? marca)
    {
        AutorizacionId = Recortar(autorizacionId);
        UltimosCuatro = ultimosCuatro is { Length: > 4 } ? ultimosCuatro[^4..] : ultimosCuatro;
        Marca = Recortar(marca);
    }

    private static string? Recortar(string? valor) =>
        valor is { Length: > LargoMaximoCodigo } ? valor[..LargoMaximoCodigo] : valor;
}
