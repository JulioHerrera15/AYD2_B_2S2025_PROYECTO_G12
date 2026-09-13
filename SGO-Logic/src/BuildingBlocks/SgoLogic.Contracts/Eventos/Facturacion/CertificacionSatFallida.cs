using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Facturacion;

/// <summary>
/// Evento 2 del contrato facturacion-eventos-v1. El documento fiscal agoto
/// <c>Sat:MaxIntentos</c> contra la SAT y pasa a un estado terminal de alerta para
/// revision manual (ADR-011, CU-04). Se publica por Outbox sobre <c>sgo.eventos</c>
/// como alerta interna: el documento no se pierde, queda persistido en estado
/// ALERTA con su XML, serie y correlativo.
/// </summary>
public static class CertificacionSatFallida
{
    public const string EventType = "CertificacionSatFallida";
    public const string RoutingKey = "facturacion.documento-fiscal.certificacion-fallida";
    public const string AggregateType = "DocumentoFiscal";
}

/// <summary>Contenido de <c>data</c> de <see cref="CertificacionSatFallida" />.</summary>
public sealed record CertificacionSatFallidaData(
    string DocumentoFiscalId,
    string OrdenId,
    TipoDocumentoFiscal TipoDocumento,
    string Serie,
    string NumeroDocumento,
    int Intentos,
    string UltimoCodigo,
    string UltimoMotivo,
    DateTime FallidaEn);
