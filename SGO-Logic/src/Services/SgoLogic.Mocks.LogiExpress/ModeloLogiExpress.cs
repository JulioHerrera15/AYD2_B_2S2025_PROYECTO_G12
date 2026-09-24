namespace SgoLogic.Mocks.LogiExpress;

/// <summary>Linea de la guia: que se transporta y cuanto.</summary>
public sealed record LineaGuia(string Sku, int Cantidad, string? Descripcion);

/// <summary>Solicitud de guia que manda Despacho al determinar el origen.</summary>
public sealed record PeticionGuia(
    string OrdenId,
    string UbicacionOrigenId,
    string DireccionEntrega,
    IReadOnlyList<LineaGuia> Lineas);

/// <summary>Guia emitida. <c>TrackingUrl</c> es el enlace que se le da al cliente.</summary>
public sealed record RespuestaGuia(
    string GuiaId,
    string TrackingUrl,
    string Transportista,
    DateTime EmitidaEn);

/// <summary>Estado de rastreo de una guia ya emitida.</summary>
public sealed record RespuestaRastreo(
    string GuiaId,
    string OrdenId,
    string Estado,
    string? Detalle,
    DateTime ActualizadoEn);

/// <summary>Guia registrada en memoria para poder rastrearla y listarla.</summary>
public sealed record GuiaRegistrada(
    string GuiaId,
    string OrdenId,
    string UbicacionOrigenId,
    string Estado,
    DateTime EmitidaEn);

/// <summary>Resultado de un intento de emision, incluidos los que no emitieron nada.</summary>
public sealed record IntentoRegistrado(
    string OrdenId,
    string Resultado,
    string? GuiaId,
    DateTime RecibidoEn);

/// <summary>Cambio de comportamiento del mock desde /admin.</summary>
public sealed record CambioModo(string Modo, int? LatenciaMs, int? UnoDeCada);

/// <summary>Empuje manual de un estado de entrega desde /admin (HU-012).</summary>
public sealed record CambioEstadoGuia(string Estado, string? Detalle);
