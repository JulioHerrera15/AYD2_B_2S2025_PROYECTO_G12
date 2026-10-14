using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Vista de lectura de una sucursal para GET /api/inventario/sucursales.</summary>
public sealed record SucursalDto(
    string Id,
    string Nombre,
    TipoUbicacion TipoUbicacion,
    string Pais,
    string Departamento,
    string Municipio,
    double? Latitud,
    double? Longitud,
    bool Activa)
{
    public static SucursalDto Desde(Sucursal sucursal) => new(
        sucursal.Id,
        sucursal.Nombre,
        sucursal.TipoUbicacion,
        sucursal.Pais,
        sucursal.Departamento,
        sucursal.Municipio,
        sucursal.Latitud,
        sucursal.Longitud,
        sucursal.Activa);
}

/// <summary>
/// Respuesta de GET /api/inventario/existencias/{sku}/cercana: la tienda mas cercana
/// a <see cref="Desde" /> que tiene el SKU disponible. <see cref="DistanciaKm" /> es
/// null cuando el origen no tiene coordenadas y la eleccion fue por sobrante.
/// </summary>
public sealed record SucursalCercanaDto(
    string Sku,
    string? Desde,
    string UbicacionId,
    string Nombre,
    string Pais,
    string Departamento,
    string Municipio,
    int Disponible,
    double? DistanciaKm)
{
    public static SucursalCercanaDto Crear(string sku, string? desde, SucursalCercana cercana) => new(
        sku,
        desde,
        cercana.Sucursal.Id,
        cercana.Sucursal.Nombre,
        cercana.Sucursal.Pais,
        cercana.Sucursal.Departamento,
        cercana.Sucursal.Municipio,
        cercana.Disponible,
        cercana.DistanciaKm is null ? null : Math.Round(cercana.DistanciaKm.Value, 1));
}
