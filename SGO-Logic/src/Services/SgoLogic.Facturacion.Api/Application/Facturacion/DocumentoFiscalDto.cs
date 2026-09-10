using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Facturacion.Api.Domain;

namespace SgoLogic.Facturacion.Api.Application.Facturacion;

/// <summary>Proyeccion de salida de un documento fiscal, para consultarlo por orden.</summary>
public sealed record DocumentoFiscalDto(
    string Id,
    string OrdenId,
    string TransactionId,
    TipoDocumentoFiscal TipoDocumento,
    string? DocumentoOrigenId,
    string CuiReceptor,
    string NombreReceptor,
    string DireccionReceptor,
    Pais PaisReceptor,
    Moneda Moneda,
    decimal Subtotal,
    decimal Impuestos,
    decimal Total,
    string Serie,
    long Correlativo,
    string NumeroDocumento,
    EstadoDocumentoFiscal Estado,
    int Intentos,
    DateTime? ProximoIntentoEn,
    string? Folio,
    string? NumeroAutorizacion,
    string? UltimoCodigo,
    string? UltimoMotivo,
    string Xml,
    DateTime CreadoEn,
    DateTime ActualizadoEn,
    DateTime? CertificadoEn,
    IReadOnlyList<DocumentoFiscalLineaDto> Lineas)
{
    public static DocumentoFiscalDto Desde(DocumentoFiscal documento) => new(
        documento.Id.ToString(),
        documento.OrdenId,
        documento.TransactionId,
        documento.TipoDocumento,
        documento.DocumentoOrigenId,
        documento.CuiReceptor,
        documento.NombreReceptor,
        documento.DireccionReceptor,
        documento.PaisReceptor,
        documento.Moneda,
        documento.Subtotal,
        documento.Impuestos,
        documento.Total,
        documento.Serie,
        documento.Correlativo,
        documento.NumeroDocumento,
        documento.Estado,
        documento.Intentos,
        documento.ProximoIntentoEn,
        documento.Folio,
        documento.NumeroAutorizacion,
        documento.UltimoCodigo,
        documento.UltimoMotivo,
        documento.Xml,
        documento.CreadoEn,
        documento.ActualizadoEn,
        documento.CertificadoEn,
        documento.Lineas
            .OrderBy(linea => linea.NumeroLinea)
            .Select(DocumentoFiscalLineaDto.Desde)
            .ToList());
}

public sealed record DocumentoFiscalLineaDto(
    int NumeroLinea,
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Impuesto)
{
    public static DocumentoFiscalLineaDto Desde(DocumentoFiscalLinea linea) => new(
        linea.NumeroLinea,
        linea.Sku,
        linea.Descripcion,
        linea.Cantidad,
        linea.PrecioUnitario,
        linea.Impuesto);
}
