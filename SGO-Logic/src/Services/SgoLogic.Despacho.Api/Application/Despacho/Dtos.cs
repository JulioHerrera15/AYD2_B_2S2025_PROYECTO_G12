using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Application.Despacho;

/// <summary>Vista de lectura de un despacho.</summary>
public sealed record DespachoDto(
    string Id,
    string OrdenId,
    string ReservaId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionOrigenId,
    TipoUbicacion? TipoUbicacionOrigen,
    string? UbicacionRecogidaId,
    string? DireccionEntrega,
    string Estado,
    string? GuiaId,
    string? TrackingUrl,
    string? Transportista,
    string? DetalleEstado,
    DateTime CreadoEn,
    DateTime? EntregadoEn,
    IReadOnlyList<LineaDespachoDto> Lineas)
{
    public static DespachoDto Desde(Domain.Despacho despacho) => new(
        despacho.Id,
        despacho.OrdenId,
        despacho.ReservaId,
        despacho.MetodoEntrega,
        despacho.UbicacionOrigenId,
        despacho.TipoUbicacionOrigen,
        despacho.UbicacionRecogidaId,
        despacho.DireccionEntrega,
        despacho.Estado.ToString(),
        despacho.GuiaId,
        despacho.TrackingUrl,
        despacho.Transportista,
        despacho.DetalleEstado,
        despacho.CreadoEn,
        despacho.EntregadoEn,
        despacho.Lineas.Select(l => new LineaDespachoDto(l.Sku, l.Descripcion, l.Cantidad)).ToList());
}

public sealed record LineaDespachoDto(string Sku, string Descripcion, int Cantidad);

/// <summary>Cuerpo del webhook de estados de entrega (HU-012).</summary>
public sealed record ActualizarEstadoRequest(string Estado, string? Detalle, DateTime? OcurridoEn);

/// <summary>Lo que Despacho necesita para nacer, venga del evento o de una llamada directa.</summary>
public sealed record SolicitudDespacho(
    string OrdenId,
    string ReservaId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionOrigenSugeridaId,
    TipoUbicacion? TipoUbicacionOrigenSugerida,
    string? UbicacionRecogidaId,
    string? DireccionEntrega,
    IReadOnlyList<LineaDespachoDto> Lineas,
    string? ClienteId = null);

/// <summary>Cambio de metodo de entrega despues de confirmada la compra.</summary>
public sealed record SolicitudCambioMetodo(
    string OrdenId,
    MetodoEntrega MetodoEntregaNuevo,
    string? UbicacionRecogidaId,
    string? DireccionEntrega);

/// <summary>Cuerpo del endpoint de reasignacion de metodo de entrega.</summary>
public sealed record CambioMetodoRequest(string MetodoEntrega, string? UbicacionRecogidaId, string? DireccionEntrega);
