using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Despacho;

/// <summary>
/// Evento 1 del contrato despacho-eventos-v1. Despacho resolvio desde donde sale el
/// pedido (HU-011). Se emite siempre, sea domicilio o recogida en tienda, y es lo
/// que dispara la instruccion de preparacion para logistica (HU-019).
/// </summary>
public static class OrigenDespachoDeterminado
{
    public const string EventType = "OrigenDespachoDeterminado";
    public const string RoutingKey = "despacho.origen.determinado";
    public const string AggregateType = "Despacho";
}

public sealed record OrigenDespachoDeterminadoData(
    string DespachoId,
    string OrdenId,
    string ReservaId,
    MetodoEntrega MetodoEntrega,
    string UbicacionOrigenId,
    TipoUbicacion TipoUbicacionOrigen,
    IReadOnlyList<LineaDespachoData> Lineas,
    DateTime DeterminadoEn);

/// <summary>
/// Evento 2. LogiExpress emitio la guia (HU-011). No se emite cuando el cliente
/// eligio recogida en tienda: ahi no hay guia que pedir.
/// </summary>
public static class GuiaAsignada
{
    public const string EventType = "GuiaAsignada";
    public const string RoutingKey = "despacho.guia.asignada";
    public const string AggregateType = "Despacho";
}

public sealed record GuiaAsignadaData(
    string DespachoId,
    string OrdenId,
    string GuiaId,
    string TrackingUrl,
    string Transportista,
    DateTime AsignadaEn);

/// <summary>
/// Evento 3. LogiExpress no respondio a tiempo (HU-011, ~2% de las solicitudes). El
/// despacho fisico continua igual: es un resultado de negocio tolerado, no un error
/// tecnico. La guia se pide despues.
/// </summary>
public static class GuiaPendiente
{
    public const string EventType = "GuiaPendiente";
    public const string RoutingKey = "despacho.guia.pendiente";
    public const string AggregateType = "Despacho";
}

public sealed record GuiaPendienteData(
    string DespachoId,
    string OrdenId,
    string UbicacionOrigenId,
    string MotivoFalla,
    DateTime OcurridoEn);

/// <summary>
/// Evento 4. Instruccion de preparacion para el operador de la ubicacion de origen
/// (HU-019). Viaja aparte de <see cref="OrigenDespachoDeterminado" /> porque el
/// consumidor es otro: alla la saga, aca el panel de logistica y Notificaciones.
/// </summary>
public static class PreparacionSolicitada
{
    public const string EventType = "PreparacionSolicitada";
    public const string RoutingKey = "despacho.preparacion.solicitada";
    public const string AggregateType = "Despacho";
}

public sealed record PreparacionSolicitadaData(
    string DespachoId,
    string OrdenId,
    string UbicacionOrigenId,
    TipoUbicacion TipoUbicacionOrigen,
    MetodoEntrega MetodoEntrega,
    IReadOnlyList<LineaDespachoData> Lineas,
    DateTime SolicitadaEn);

/// <summary>
/// Evento 5. Cambio de estado de entrega comunicado por LogiExpress (HU-012). Lo
/// consumen Ordenes (para avanzar la orden), Consulta de Estado y Notificaciones.
/// </summary>
public static class DespachoEstadoActualizado
{
    public const string EventType = "DespachoEstadoActualizado";
    public const string RoutingKey = "despacho.estado.actualizado";
    public const string AggregateType = "Despacho";
}

public sealed record DespachoEstadoActualizadoData(
    string DespachoId,
    string OrdenId,
    string EstadoAnterior,
    string EstadoNuevo,
    string? Detalle,
    DateTime ActualizadoEn);

/// <summary>
/// Evento 6. El pedido quedo listo para que el cliente lo recoja en tienda (HU-020).
/// Se emite al pasar a LISTA_PARA_RECOGER y es lo que Notificaciones usa para
/// avisarle al cliente que ya puede pasar.
/// </summary>
public static class PedidoListoParaRecoger
{
    public const string EventType = "PedidoListoParaRecoger";
    public const string RoutingKey = "despacho.pedido.listo-para-recoger";
    public const string AggregateType = "Despacho";
}

public sealed record PedidoListoParaRecogerData(
    string DespachoId,
    string OrdenId,
    string UbicacionRecogidaId,
    DateTime ListoEn);

/// <summary>Linea de un despacho tal como viaja en los eventos.</summary>
public sealed record LineaDespachoData(string Sku, string Descripcion, int Cantidad);
