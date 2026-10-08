namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Estado de un abono. SOLICITADO y PENDIENTE estan "en curso" (solo uno por
/// cuenta); APLICADO, RECHAZADO, REEMBOLSO_PENDIENTE y REEMBOLSADO son finales.
/// </summary>
public enum EstadoAbonoCredito
{
    SOLICITADO,

    /// <summary>El cobro quedo sin resultado cierto; se puede reintentar con el mismo abono.</summary>
    PENDIENTE,

    APLICADO,
    RECHAZADO,

    /// <summary>Se cobro, pero la cuenta ya estaba anulada (o se anulo): hay que devolverlo completo.</summary>
    REEMBOLSO_PENDIENTE,

    /// <summary>Lo cobrado se devolvio completo a la tarjeta (MontoReembolsado = Monto).</summary>
    REEMBOLSADO
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

    /// <summary>Lo ya devuelto a la tarjeta (confirmado por Pagos). Nunca mayor que Monto.</summary>
    public decimal MontoReembolsado { get; private set; }

    /// <summary>
    /// Lo pedido a Pagos para devolver (en curso mas confirmado). Impide pedir dos veces
    /// el mismo dinero; una solicitud fallida sigue contando hasta la atencion manual.
    /// </summary>
    public decimal MontoReembolsoSolicitado { get; private set; }

    /// <summary>Ultima vez que se pidio un reembolso de este abono. Null si nunca se pidio.</summary>
    public DateTime? ReembolsoSolicitadoEn { get; private set; }

    /// <summary>Un reembolso de este abono fallo: requiere atencion manual. Null si no.</summary>
    public DateTime? ReembolsoFallidoEn { get; private set; }

    public string? MotivoFallaReembolso { get; private set; }

    /// <summary>Concurrencia optimista.</summary>
    public int Version { get; private set; }

    /// <summary>Lo cobrado que aun no se ha pedido devolver.</summary>
    public decimal PendientePorSolicitarReembolso => Math.Max(0m, Monto - MontoReembolsoSolicitado);

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
    /// este abono aplicado se anulo. Hay que devolverlo completo a la tarjeta.
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

    /// <summary>Se pidio a Pagos devolver <paramref name="monto" /> de este abono.</summary>
    public void RegistrarSolicitudReembolso(decimal monto, DateTime ahora)
    {
        if (monto <= 0m || monto > PendientePorSolicitarReembolso)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El reembolso debe ser mayor que 0 y no mayor que lo cobrado sin reembolsar.");
        }

        MontoReembolsoSolicitado += monto;
        ReembolsoSolicitadoEn = ahora;
        Version++;
    }

    /// <summary>
    /// Pagos confirmo un reembolso de este abono. Si ya se devolvio todo lo cobrado pasa a
    /// REEMBOLSADO. Devuelve true si quedo completo.
    /// </summary>
    public bool AplicarReembolso(decimal monto, DateTime ahora)
    {
        if (monto > 0m)
        {
            MontoReembolsado = Math.Min(Monto, MontoReembolsado + monto);
            MontoReembolsoSolicitado = Math.Max(MontoReembolsoSolicitado, MontoReembolsado);
        }

        var completo = MontoReembolsado >= Monto;
        if (completo)
        {
            Estado = EstadoAbonoCredito.REEMBOLSADO;
            ReembolsoFallidoEn = null;
            MotivoFallaReembolso = null;
        }

        ResueltoEn ??= ahora;
        Version++;
        return completo;
    }

    /// <summary>Un reembolso de este abono no se pudo completar: queda para atencion manual.</summary>
    public void MarcarReembolsoFallido(string? motivo, DateTime ahora)
    {
        ReembolsoFallidoEn = ahora;
        MotivoFallaReembolso = motivo is { Length: > LargoMaximoMotivoFalla } ? motivo[..LargoMaximoMotivoFalla] : motivo;
        Version++;
    }

    public const int LargoMaximoMotivoFalla = 300;

    private void RegistrarTarjeta(string autorizacionId, string? ultimosCuatro, string? marca)
    {
        AutorizacionId = Recortar(autorizacionId);
        UltimosCuatro = ultimosCuatro is { Length: > 4 } ? ultimosCuatro[^4..] : ultimosCuatro;
        Marca = Recortar(marca);
    }

    private static string? Recortar(string? valor) =>
        valor is { Length: > LargoMaximoCodigo } ? valor[..LargoMaximoCodigo] : valor;
}
