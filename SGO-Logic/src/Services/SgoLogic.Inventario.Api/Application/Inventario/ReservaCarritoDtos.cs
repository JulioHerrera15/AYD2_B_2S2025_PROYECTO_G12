using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Cuerpo de PUT /interno/inventario/carritos/{carritoId}/items/{sku}: cantidad total deseada en el carrito.</summary>
public sealed record RetenerItemCarritoRequest(int Cantidad, string? UbicacionPreferidaId = null);

/// <summary>Cuerpo de POST /interno/inventario/carritos/{carritoId}/transferir.</summary>
public sealed record TransferirCarritoRequest(string DestinoCarritoId);

/// <summary>
/// Cuerpo opcional de POST /interno/inventario/carritos/{carritoId}/renovar. Si
/// trae <see cref="Skus" />, solo esas lineas se renuevan y las demas se cierran:
/// Auth manda los SKU que de verdad siguen en su carrito.
/// </summary>
public sealed record RenovarCarritoRequest(IReadOnlyList<string>? Skus = null);

/// <summary>
/// Una linea del carrito vista desde Inventario. <see cref="SucursalSugerida" />
/// solo viene en una linea EXPIRADA que ya no se pudo reactivar donde estaba.
/// </summary>
public sealed record LineaReservaCarritoDto(
    string Sku,
    string UbicacionId,
    string NombreUbicacion,
    TipoUbicacion TipoUbicacion,
    int Cantidad,
    DateTime ExpiraEn,
    string Estado,
    bool EnOtraSucursal,
    SucursalCercanaDto? SucursalSugerida = null)
{
    public static LineaReservaCarritoDto Desde(
        ReservaCarrito retencion,
        IReadOnlyDictionary<string, Sucursal> sucursales,
        SucursalCercanaDto? sugerida = null) => new(
        retencion.Sku,
        retencion.UbicacionId,
        sucursales.TryGetValue(retencion.UbicacionId, out var sucursal) ? sucursal.Nombre : retencion.UbicacionId,
        retencion.TipoUbicacion,
        retencion.Cantidad,
        retencion.ExpiraEn,
        retencion.Estado.ToString(),
        retencion.EnOtraSucursal,
        sugerida);
}

/// <summary>Retenciones vigentes de un carrito. <see cref="ExpiraEn" /> es la menor de las lineas ACTIVA, o null si no hay.</summary>
public sealed record ReservaCarritoDto(
    string CarritoId,
    DateTime? ExpiraEn,
    IReadOnlyList<LineaReservaCarritoDto> Lineas);

/// <summary>
/// Cuerpo del 409 cuando no hay stock para retener: el sku, cuanto hay como maximo
/// en una sola ubicacion y, si alguna tienda tiene al menos una unidad, la mas
/// cercana a la sucursal del cliente.
/// </summary>
public sealed record SinStockCarritoDto(
    string Sku,
    int Disponible,
    string Mensaje,
    SucursalCercanaDto? SucursalCercana);
