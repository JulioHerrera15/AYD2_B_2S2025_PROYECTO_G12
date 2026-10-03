using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Escritura del contexto Credito: historial, perfil y linea de un cliente.
/// GuardarCambiosAsync confirma todo lo pendiente (incluido el outbox) en un solo
/// SaveChanges, es decir, en una sola transaccion.
/// </summary>
public interface IRepositorioCredito
{
    Task<HistorialCliente?> ObtenerHistorialAsync(string clienteId, CancellationToken cancelacion);

    Task<PerfilCrediticio?> ObtenerPerfilAsync(string clienteId, CancellationToken cancelacion);

    Task<LineaCredito?> ObtenerLineaAsync(string clienteId, CancellationToken cancelacion);

    void AgregarHistorial(HistorialCliente historial);

    void AgregarPerfil(PerfilCrediticio perfil);

    void AgregarLinea(LineaCredito linea);

    Task<bool> HayHistorialesAsync(CancellationToken cancelacion);

    Task<DecisionCredito?> ObtenerDecisionAsync(string ordenId, CancellationToken cancelacion);

    Task<CuentaPorCobrar?> ObtenerCuentaPorOrdenAsync(string ordenId, CancellationToken cancelacion);

    void AgregarDecision(DecisionCredito decision);

    void AgregarCuenta(CuentaPorCobrar cuenta);

    Task<CuentaPorCobrar?> ObtenerCuentaAsync(Guid cuentaId, CancellationToken cancelacion);

    /// <summary>
    /// Todas las cuentas del cliente, con seguimiento. Si alguna ya estaba cargada y
    /// modificada en esta unidad de trabajo se devuelve esa misma instancia, asi el
    /// comportamiento propio y la mora se calculan con los cambios aun sin guardar.
    /// </summary>
    Task<IReadOnlyList<CuentaPorCobrar>> ListarCuentasClienteAsync(string clienteId, CancellationToken cancelacion);

    /// <summary>Ids de cuentas VIGENTE con saldo que vencen a mas tardar en <paramref name="limite" /> y sin recordatorio.</summary>
    Task<IReadOnlyList<Guid>> ListarCuentasParaRecordatorioAsync(DateTime limite, int maximo, CancellationToken cancelacion);

    /// <summary>Ids de cuentas VIGENTE con saldo cuyo VenceEn ya paso.</summary>
    Task<IReadOnlyList<Guid>> ListarCuentasPorVencerseAsync(DateTime ahora, int maximo, CancellationToken cancelacion);

    Task<AbonoCredito?> ObtenerAbonoAsync(Guid abonoId, CancellationToken cancelacion);

    Task<bool> HayAbonoEnCursoAsync(Guid cuentaId, CancellationToken cancelacion);

    Task<IReadOnlyList<AbonoCredito>> ListarAbonosAplicadosAsync(Guid cuentaId, CancellationToken cancelacion);

    void AgregarAbono(AbonoCredito abono);

    /// <summary>True si la cuenta tiene algun abono que pago deuda (APLICADO o ya REEMBOLSADO).</summary>
    Task<bool> HayAbonosCobradosAsync(Guid cuentaId, CancellationToken cancelacion);

    /// <summary>Ids de abonos REEMBOLSO_PENDIENTE a los que aun no se les pidio el reembolso.</summary>
    Task<IReadOnlyList<Guid>> ListarAbonosReembolsoSinSolicitudAsync(int maximo, CancellationToken cancelacion);

    /// <summary>True si ya existe el movimiento (idempotencia por devolucionId o reembolsoId).</summary>
    Task<bool> ExisteMovimientoAsync(TipoMovimientoCuenta tipo, string referencia, CancellationToken cancelacion);

    void AgregarMovimiento(MovimientoCuenta movimiento);

    /// <summary>Siguiente valor de la secuencia de referencias de abono.</summary>
    Task<long> SiguienteCorrelativoAbonoAsync(CancellationToken cancelacion);

    /// <summary>Clientes con historial, para recalcular perfiles tras el reinicio de demo.</summary>
    Task<IReadOnlyList<string>> ListarClientesConHistorialAsync(CancellationToken cancelacion);

    /// <summary>
    /// Confirma lo pendiente. Lanza <see cref="ExcepcionConcurrenciaCredito" /> si otra
    /// operacion cambio la misma linea o cuenta (token Version) entre la lectura y el
    /// guardado, y <see cref="ExcepcionDuplicadoCredito" /> si choca con un indice unico
    /// (por ejemplo, un segundo abono en curso para la misma cuenta).
    /// </summary>
    Task GuardarCambiosAsync(CancellationToken cancelacion);

    /// <summary>Olvida todo lo pendiente y lo leido, para reintentar releyendo de la base.</summary>
    void DescartarCambios();

    /// <summary>
    /// Reinicio de demo: borra movimientos, abonos, cuentas por cobrar y decisiones, deja el saldo
    /// utilizado de todas las lineas en 0 y quita la pausa por mora. No toca perfiles ni
    /// historiales (el servicio los recalcula despues).
    /// </summary>
    Task<ResultadoReinicioDemo> ReiniciarDemoAsync(DateTime ahora, CancellationToken cancelacion);
}

/// <summary>Otra operacion modifico la misma linea o cuenta: hay que releer y reintentar.</summary>
public sealed class ExcepcionConcurrenciaCredito : Exception
{
    public ExcepcionConcurrenciaCredito(string mensaje, Exception? interna = null)
        : base(mensaje, interna)
    {
    }
}

/// <summary>Un indice unico rechazo el cambio (por ejemplo, otro abono en curso para la cuenta).</summary>
public sealed class ExcepcionDuplicadoCredito : Exception
{
    public ExcepcionDuplicadoCredito(string mensaje, Exception? interna = null)
        : base(mensaje, interna)
    {
    }
}

public sealed record ResultadoReinicioDemo(
    int CuentasBorradas, int DecisionesBorradas, int LineasReiniciadas, int AbonosBorrados = 0, int MovimientosBorrados = 0);

/// <summary>
/// Lectura para la consulta de oferta y "Mi credito": perfil, linea y antiguedad
/// del cliente en una sola consulta, sin seguimiento de cambios.
/// </summary>
public interface IConsultasCredito
{
    Task<ResumenCredito?> ObtenerResumenAsync(string clienteId, CancellationToken cancelacion);

    /// <summary>Cuentas por cobrar del cliente, sin seguimiento de cambios.</summary>
    Task<IReadOnlyList<CompraCredito>> ListarComprasAsync(string clienteId, CancellationToken cancelacion);

    /// <summary>Abonos del cliente (todas sus cuentas), sin seguimiento de cambios.</summary>
    Task<IReadOnlyList<AbonoCredito>> ListarAbonosAsync(string clienteId, CancellationToken cancelacion);

    /// <summary>Movimientos (devoluciones y reembolsos) de las cuentas del cliente, sin seguimiento.</summary>
    Task<IReadOnlyList<MovimientoCuenta>> ListarMovimientosAsync(string clienteId, CancellationToken cancelacion);
}

/// <summary>Proyeccion de lectura de una cuenta por cobrar.</summary>
public sealed record CompraCredito(
    string OrdenId,
    string NumeroOrden,
    decimal MontoOriginal,
    decimal SaldoPendiente,
    DateTime EmitidaEn,
    DateTime VenceEn,
    EstadoCuentaPorCobrar Estado,
    Guid CuentaId = default,
    DateTime? PagadaEn = null,
    bool? PagoPuntual = null,
    string? MotivoAnulacion = null);

/// <summary>Proyeccion de lectura. Null en la linea si aun no existe; null en ClienteDesde si no hay historial.</summary>
public sealed record ResumenCredito(
    string ClienteId,
    NivelCredito Nivel,
    bool Elegible,
    string? CodigoMotivo,
    decimal PuntosPuntualidad,
    decimal PuntosMonto,
    decimal PuntosAntiguedad,
    decimal PuntosFrecuencia,
    decimal PuntosDevoluciones,
    decimal PuntosIncidentes,
    DateTime CalculadoEn,
    decimal? LimiteAprobado,
    decimal? SaldoUtilizado,
    string? Moneda,
    EstadoLineaCredito? EstadoLinea,
    DateTime? ActualizadoLineaEn,
    DateTime? ClienteDesde);
