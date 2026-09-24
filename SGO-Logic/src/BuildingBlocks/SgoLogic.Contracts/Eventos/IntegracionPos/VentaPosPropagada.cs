namespace SgoLogic.Contracts.Eventos.IntegracionPos;

public static class VentaPosPropagada
{
    public const string EventType = "VentaPosPropagada";
    public const string RoutingKey = "integracion-pos.venta-pos.propagada";
    public const string AggregateType = "VentaPos";
}

public sealed record VentaPosPropagadaData(
    string OrdenId,
    string NumeroOrden,
    string TiendaId,
    string PosInstanciaId,
    string EventoIdOriginal,
    string Tipo,
    string Estado,
    decimal Total,
    DateTime PropagadaEn,
    string? Detalle);
