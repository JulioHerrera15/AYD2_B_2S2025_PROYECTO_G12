namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>Precio vigente de un SKU segun el catalogo de Inventario.</summary>
public sealed record ProductoCatalogo(string Sku, decimal Precio, bool Activo);

/// <summary>
/// Puerto hacia el catalogo de productos de Inventario (GET /api/inventario/productos/{sku}),
/// la fuente del precio (B1). La implementacion real cachea unos segundos cada SKU.
/// </summary>
public interface ICatalogoProductos
{
    /// <summary>
    /// El producto, o null si el SKU no existe. Lanza
    /// <see cref="ExcepcionCatalogoNoDisponible" /> si Inventario no responde.
    /// </summary>
    Task<ProductoCatalogo?> ObtenerAsync(string sku, CancellationToken cancelacion);
}

/// <summary>Inventario no respondio al validar los precios de la orden: la orden no se crea (503).</summary>
public sealed class ExcepcionCatalogoNoDisponible : Exception
{
    public ExcepcionCatalogoNoDisponible(string mensaje, Exception? interna = null) : base(mensaje, interna)
    {
    }
}

/// <summary>
/// Una linea de la orden no coincide con el catalogo: el SKU no existe, esta inactivo
/// o su precio unitario no es el vigente. La orden no se crea (422 con el SKU).
/// </summary>
public sealed class ExcepcionPrecioNoValido : Exception
{
    public ExcepcionPrecioNoValido(string sku, string mensaje, decimal? precioVigente = null) : base(mensaje)
    {
        Sku = sku;
        PrecioVigente = precioVigente;
    }

    public string Sku { get; }

    /// <summary>Precio del catalogo, si el SKU existe.</summary>
    public decimal? PrecioVigente { get; }
}
