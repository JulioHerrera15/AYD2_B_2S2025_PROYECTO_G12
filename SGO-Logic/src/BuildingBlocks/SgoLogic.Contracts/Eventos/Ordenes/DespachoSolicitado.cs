using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 7 del contrato ordenes-eventos-v1. Ordenes pide a Despacho determinar el
/// origen y coordinar la guia (CU-11). Se emite al pasar la orden a
/// DESPACHO_SOLICITADO.
/// </summary>
public static class DespachoSolicitado
{
    public const string EventType = "DespachoSolicitado";
    public const string RoutingKey = "ordenes.despacho.solicitado";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento DespachoSolicitado (seccion 7.7 del contrato).</summary>
/// <param name="ClienteId">
/// Cliente de la orden (su email). Nullable y al final: Despacho lo guarda para
/// incluirlo en PedidoListoParaRecoger y que Notificaciones avise al cliente.
/// </param>
public sealed record DespachoSolicitadoData(
    string OrdenId,
    string ReservaId,
    MetodoEntrega MetodoEntrega,
    UbicacionOrigenSugeridaData? UbicacionOrigenSugerida,
    string? UbicacionRecogidaId,
    DireccionEntregaData? DireccionEntrega,
    IReadOnlyList<DespachoLineaData> Lineas,
    DateTime SolicitadoEn,
    string? ClienteId = null);

public sealed record UbicacionOrigenSugeridaData(string Id, TipoUbicacion Tipo);

public sealed record DespachoLineaData(string Sku, string Descripcion, int Cantidad);
