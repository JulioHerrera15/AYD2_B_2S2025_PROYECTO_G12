namespace SgoLogic.Contracts.Eventos.IntegracionErp;

/// <summary>
/// Historial comercial de un cliente tomado del ERP. Lo publica Integracion ERP (un
/// evento por cliente que cambio) y lo consume Credito para recalcular el perfil
/// crediticio. aggregateId = email del cliente en minusculas.
/// </summary>
public static class HistorialClienteErpActualizado
{
    public const string EventType = "HistorialClienteErpActualizado";
    public const string RoutingKey = "integracion-erp.cliente.historial-actualizado";
    public const string AggregateType = "ClienteErp";
}

/// <param name="ClienteId">Email del cliente en minusculas (mismo identificador que Ordenes y Pagos).</param>
/// <param name="FechaCorte">Fecha de corte del ERP: un historial con fecha de corte menor o igual a la ya aplicada se ignora.</param>
public sealed record HistorialClienteErpActualizadoData(
    string ClienteId,
    string CodigoClienteErp,
    DateTime ClienteDesde,
    int ComprasUltimos12Meses,
    decimal MontoCompradoUltimos12Meses,
    string Moneda,
    int PagosPuntuales,
    int PagosTardios,
    int MaxDiasAtraso,
    int Devoluciones,
    int IncidentesGraves,
    int OtrosIncidentes,
    DateTime FechaCorte,
    string? LoteOrigen);
