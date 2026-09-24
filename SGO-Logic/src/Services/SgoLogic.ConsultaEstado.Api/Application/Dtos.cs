namespace SgoLogic.ConsultaEstado.Api.Application;

// ── Respuesta de consulta de estado ───────────────────────────────────────────

public sealed record IncidenciaDto(
    Guid Id,
    string Estado,
    string Descripcion,
    string? Resolucion,
    DateTime CreadaEn,
    DateTime? ActualizadaEn);

public sealed record OrdenEstadoDto(
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    string Canal,
    string Estado,
    string? EstadoAnterior,
    string MetodoEntrega,
    string Moneda,
    decimal Total,
    string? PagoId,
    string? TiendaDestinoId,
    string? DocumentoFiscalId,
    string? MotivoUltimoCambio,
    DateTime? CreadaEn,
    DateTime ActualizadaEn,
    IReadOnlyList<IncidenciaDto> Incidencias);

public sealed record OrdenResumenDto(
    string OrdenId,
    string NumeroOrden,
    string Estado,
    decimal Total,
    DateTime ActualizadaEn,
    string? ClienteId = null);

public sealed record HistorialOrdenesDto(
    string ClienteId,
    int Pagina,
    int Tamano,
    int TotalRegistros,
    IReadOnlyList<OrdenResumenDto> Ordenes);

// ── Incidencias ───────────────────────────────────────────────────────────────

public sealed record CrearIncidenciaRequest(
    string OrdenId,
    string Descripcion);

public sealed record ActualizarIncidenciaRequest(
    string? Descripcion,
    string? Estado,
    string? Resolucion);
