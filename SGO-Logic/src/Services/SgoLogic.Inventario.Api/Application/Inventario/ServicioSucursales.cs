using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Lectura del catalogo de sucursales y busqueda de la tienda mas cercana con stock
/// (<see cref="SelectorSucursalCercana" />). No escribe nada.
/// </summary>
public sealed class ServicioSucursales
{
    private readonly IRepositorioSucursales _sucursales;
    private readonly IRepositorioExistencias _existencias;

    public ServicioSucursales(IRepositorioSucursales sucursales, IRepositorioExistencias existencias)
    {
        _sucursales = sucursales;
        _existencias = existencias;
    }

    public async Task<IReadOnlyList<SucursalDto>> ListarActivasAsync(CancellationToken cancelacion)
    {
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);
        return sucursales.Select(SucursalDto.Desde).ToList();
    }

    /// <summary>
    /// Tienda activa mas cercana a <paramref name="desde" /> con al menos
    /// <paramref name="cantidad" /> unidades disponibles del SKU, excluyendo la propia
    /// <paramref name="desde" />. Si <paramref name="desde" /> es una sucursal conocida,
    /// solo busca en su mismo pais. Null si ninguna tienda tiene.
    /// </summary>
    public async Task<SucursalCercanaDto?> BuscarCercanaAsync(
        string sku,
        string? desde,
        int cantidad,
        CancellationToken cancelacion)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        }

        var existencias = await _existencias.ListarPorSkuAsync(sku, cancelacion);
        var sucursales = await _sucursales.ListarActivasAsync(cancelacion);

        var cercana = SelectorSucursalCercana.Elegir(
            new[] { new LineaReservada(sku, cantidad) },
            existencias,
            sucursales,
            desde);

        return cercana is null ? null : SucursalCercanaDto.Crear(sku, desde, cercana);
    }
}
