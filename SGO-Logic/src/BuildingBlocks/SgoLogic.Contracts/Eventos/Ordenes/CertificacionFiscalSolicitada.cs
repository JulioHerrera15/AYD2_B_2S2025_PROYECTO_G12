using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 6 del contrato ordenes-eventos-v1. Ordenes pide a Facturacion generar y
/// certificar el documento ante la SAT de forma asincrona (CU-04, ADR-011,
/// RES-07). Se emite al pasar la orden a CONFIRMADA_PENDIENTE_FISCAL, junto con
/// OrdenConfirmada.
/// </summary>
public static class CertificacionFiscalSolicitada
{
    public const string EventType = "CertificacionFiscalSolicitada";
    public const string RoutingKey = "ordenes.fiscal.certificacion-solicitada";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento CertificacionFiscalSolicitada (seccion 7.6 del contrato).</summary>
public sealed record CertificacionFiscalSolicitadaData(
    string OrdenId,
    TipoDocumentoFiscal TipoDocumento,
    string? DocumentoOrigenId,
    ReceptorFiscalData Receptor,
    Moneda Moneda,
    decimal Subtotal,
    decimal Impuestos,
    decimal Total,
    IReadOnlyList<CertificacionFiscalLineaData> Lineas,
    DateTime SolicitadaEn);

public sealed record ReceptorFiscalData(
    string Cui,
    string Nombre,
    string Direccion,
    Pais Pais);

public sealed record CertificacionFiscalLineaData(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Impuesto);
