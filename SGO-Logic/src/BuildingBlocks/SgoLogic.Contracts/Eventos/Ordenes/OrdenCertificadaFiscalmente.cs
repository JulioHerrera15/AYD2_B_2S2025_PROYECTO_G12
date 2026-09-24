using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 12 del contrato ordenes-eventos-v1. Se emite cuando la SAT devuelve la
/// certificacion (consumida de <c>facturacion.documento-fiscal.certificado</c>) y
/// la orden pasa de <c>CONFIRMADA_PENDIENTE_FISCAL</c> a <c>CONFIRMADA</c>.
/// Portador unico del folio fiscal; cierra las objeciones 7 y 10 de v1.1.
/// Prohibido como fuente de estado (seccion 6 del contrato): la transicion viaja
/// en su <c>OrdenEstadoCambiado</c>, este evento solo entrega el dato fiscal.
/// </summary>
public static class OrdenCertificadaFiscalmente
{
    public const string EventType = "OrdenCertificadaFiscalmente";
    public const string RoutingKey = "ordenes.orden.certificada-fiscalmente";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento OrdenCertificadaFiscalmente (seccion 7.12 del contrato).</summary>
public sealed record OrdenCertificadaFiscalmenteData(
    string OrdenId,
    string NumeroOrden,
    string? TiendaDestinoId,
    string DocumentoFiscalId,
    TipoDocumentoFiscal TipoDocumento,
    string Serie,
    string NumeroDocumento,
    string NumeroAutorizacion,
    Moneda Moneda,
    decimal Total,
    DateTime CertificadaEn);
