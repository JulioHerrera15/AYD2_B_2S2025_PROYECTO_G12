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

    Task GuardarCambiosAsync(CancellationToken cancelacion);
}

/// <summary>
/// Lectura para la consulta de oferta y "Mi credito": perfil, linea y antiguedad
/// del cliente en una sola consulta, sin seguimiento de cambios.
/// </summary>
public interface IConsultasCredito
{
    Task<ResumenCredito?> ObtenerResumenAsync(string clienteId, CancellationToken cancelacion);
}

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
