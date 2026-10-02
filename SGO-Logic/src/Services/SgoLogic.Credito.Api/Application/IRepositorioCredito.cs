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

    /// <summary>
    /// Confirma lo pendiente. Lanza <see cref="ExcepcionConcurrenciaCredito" /> si otra
    /// operacion cambio la misma linea o cuenta (token Version) entre la lectura y el guardado.
    /// </summary>
    Task GuardarCambiosAsync(CancellationToken cancelacion);

    /// <summary>Olvida todo lo pendiente y lo leido, para reintentar releyendo de la base.</summary>
    void DescartarCambios();

    /// <summary>
    /// Reinicio de demo: borra cuentas por cobrar y decisiones y deja el saldo utilizado
    /// de todas las lineas en 0. No toca perfiles ni historiales.
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

public sealed record ResultadoReinicioDemo(int CuentasBorradas, int DecisionesBorradas, int LineasReiniciadas);

/// <summary>
/// Lectura para la consulta de oferta y "Mi credito": perfil, linea y antiguedad
/// del cliente en una sola consulta, sin seguimiento de cambios.
/// </summary>
public interface IConsultasCredito
{
    Task<ResumenCredito?> ObtenerResumenAsync(string clienteId, CancellationToken cancelacion);

    /// <summary>Cuentas por cobrar del cliente, sin seguimiento de cambios.</summary>
    Task<IReadOnlyList<CompraCredito>> ListarComprasAsync(string clienteId, CancellationToken cancelacion);
}

/// <summary>Proyeccion de lectura de una cuenta por cobrar.</summary>
public sealed record CompraCredito(
    string OrdenId,
    string NumeroOrden,
    decimal MontoOriginal,
    decimal SaldoPendiente,
    DateTime EmitidaEn,
    DateTime VenceEn,
    EstadoCuentaPorCobrar Estado);

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
