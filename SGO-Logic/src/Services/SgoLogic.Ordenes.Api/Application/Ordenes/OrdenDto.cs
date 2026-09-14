using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Proyeccion de salida de una orden. Se construye desde el agregado con
/// <see cref="Desde" />; las entidades del dominio no cruzan el borde HTTP.
/// </summary>
public sealed record OrdenDto(
    string Id,
    string NumeroOrden,
    string ClienteId,
    Canal Canal,
    MetodoEntrega MetodoEntrega,
    Moneda Moneda,
    MedioPago MedioPago,
    EstadoOrden Estado,
    int Secuencia,
    decimal Impuestos,
    decimal Subtotal,
    decimal Total,
    string? UbicacionSolicitadaId,
    DateTime CreadaEn,
    string? ReservaId,
    string? PagoId,
    DatosFiscalesDto? DatosFiscales,
    AlertaFiscalDto? AlertaFiscal,
    IReadOnlyList<LineaOrdenDto> Lineas,
    IReadOnlyList<TransicionOrdenDto> Transiciones)
{
    public static OrdenDto Desde(Orden orden) => new(
        orden.Id,
        orden.NumeroOrden,
        orden.ClienteId,
        orden.Canal,
        orden.MetodoEntrega,
        orden.Moneda,
        orden.MedioPago,
        orden.Estado,
        orden.Secuencia,
        orden.Impuestos,
        orden.Subtotal,
        orden.Total,
        orden.UbicacionSolicitadaId,
        orden.CreadaEn,
        orden.ReservaId,
        orden.PagoId,
        orden.DocumentoFiscalId is not null
            ? new DatosFiscalesDto(
                orden.DocumentoFiscalId,
                TipoDocumentoFiscal.FACTURA,
                orden.DocumentoFiscalSerie ?? string.Empty,
                orden.DocumentoFiscalNumero ?? string.Empty,
                orden.DocumentoFiscalNumeroAutorizacion ?? string.Empty,
                orden.DocumentoFiscalFolio,
                orden.DocumentoFiscalCertificadoEn)
            : null,
        orden.AlertaFiscal
            ? new AlertaFiscalDto(
                orden.AlertaFiscalEn!.Value,
                orden.AlertaFiscalMotivo ?? string.Empty)
            : null,
        orden.Lineas.Select(LineaOrdenDto.Desde).ToList(),
        orden.Transiciones
            .OrderBy(transicion => transicion.Secuencia)
            .Select(TransicionOrdenDto.Desde)
            .ToList());
}

public sealed record LineaOrdenDto(
    Guid Id,
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Subtotal)
{
    public static LineaOrdenDto Desde(LineaOrden linea) => new(
        linea.Id,
        linea.Sku,
        linea.Descripcion,
        linea.Cantidad,
        linea.PrecioUnitario,
        linea.Subtotal);
}

public sealed record TransicionOrdenDto(
    Guid Id,
    int Secuencia,
    EstadoOrden EstadoAnterior,
    EstadoOrden EstadoNuevo,
    string? Motivo,
    PasoSaga? PasoSaga,
    bool EsCompensacion,
    DateTime CambiadoEn)
{
    public static TransicionOrdenDto Desde(TransicionOrden transicion) => new(
        transicion.Id,
        transicion.Secuencia,
        transicion.EstadoAnterior,
        transicion.EstadoNuevo,
        transicion.Motivo,
        transicion.PasoSaga,
        transicion.EsCompensacion,
        transicion.CambiadoEn);
}

/// <summary>Datos fiscales de la orden una vez certificada por la SAT (evento 12).</summary>
public sealed record DatosFiscalesDto(
    string DocumentoFiscalId,
    TipoDocumentoFiscal TipoDocumento,
    string Serie,
    string NumeroDocumento,
    string NumeroAutorizacion,
    string? Folio,
    DateTime? CertificadaEn);

/// <summary>Alerta fiscal de la orden cuando la SAT agoto los reintentos de certificacion (evento 2).</summary>
public sealed record AlertaFiscalDto(
    DateTime AlertaFiscalEn,
    string Motivo);
