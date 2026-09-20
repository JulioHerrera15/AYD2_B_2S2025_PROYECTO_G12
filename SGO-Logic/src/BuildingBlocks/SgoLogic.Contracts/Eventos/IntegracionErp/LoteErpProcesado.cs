namespace SgoLogic.Contracts.Eventos.IntegracionErp;

public static class LoteErpProcesado
{
    public const string EventType = "LoteErpProcesado";
    public const string RoutingKey = "integracion-erp.lote-erp.procesado";
    public const string AggregateType = "LoteErp";
}

public sealed record LoteErpProcesadoData(
    string LoteId,
    long Secuencia,
    string Estado,
    int CantidadRegistros,
    decimal MontoTotal,
    string? Detalle,
    DateTime ProcesadoEn);
