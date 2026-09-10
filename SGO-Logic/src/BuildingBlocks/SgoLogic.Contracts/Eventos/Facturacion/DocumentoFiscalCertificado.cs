using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Facturacion;

/// <summary>
/// Evento 1 del contrato facturacion-eventos-v1. La SAT devolvio la certificacion
/// del documento fiscal: el documento queda con folio y numero de autorizacion
/// definitivos. Se publica por Outbox sobre <c>sgo.eventos</c>. En el bloque
/// siguiente lo consumira Ordenes para emitir <c>OrdenCertificadaFiscalmente</c>
/// (evento 12 de ordenes-eventos-v1) y pasar la orden a <c>CONFIRMADA</c>.
/// </summary>
public static class DocumentoFiscalCertificado
{
    public const string EventType = "DocumentoFiscalCertificado";
    public const string RoutingKey = "facturacion.documento-fiscal.certificado";
    public const string AggregateType = "DocumentoFiscal";
}

/// <summary>Contenido de <c>data</c> de <see cref="DocumentoFiscalCertificado" />.</summary>
public sealed record DocumentoFiscalCertificadoData(
    string DocumentoFiscalId,
    string OrdenId,
    TipoDocumentoFiscal TipoDocumento,
    string Serie,
    string NumeroDocumento,
    string NumeroAutorizacion,
    string Folio,
    Moneda Moneda,
    decimal Total,
    DateTime CertificadaEn);
