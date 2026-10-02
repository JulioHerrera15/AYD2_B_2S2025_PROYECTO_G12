using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Puerto de persistencia del catalogo de <see cref="Producto" />.</summary>
public interface IRepositorioProductos
{
    /// <summary>Productos activos ordenados por SKU.</summary>
    Task<IReadOnlyList<Producto>> ListarActivosAsync(CancellationToken cancelacion);

    /// <summary>El producto con ese SKU (activo o no), con seguimiento para modificarlo.</summary>
    Task<Producto?> ObtenerAsync(string sku, CancellationToken cancelacion);

    Task AgregarAsync(Producto producto, CancellationToken cancelacion);

    Task GuardarCambiosAsync(CancellationToken cancelacion);
}

/// <summary>Vista de lectura de un producto (GET /api/inventario/productos).</summary>
public sealed record ProductoDto(
    string Sku,
    string Nombre,
    string Categoria,
    decimal Precio,
    string Icono,
    bool Activo,
    string? Descripcion,
    string? Etiqueta)
{
    public static ProductoDto Desde(Producto producto) => new(
        producto.Sku,
        producto.Nombre,
        producto.Categoria,
        producto.Precio,
        producto.Icono,
        producto.Activo,
        producto.Descripcion,
        producto.Etiqueta);
}

/// <summary>Cuerpo de PUT /api/inventario/productos/{sku} (alta o actualizacion del catalogo).</summary>
public sealed record RegistrarProductoRequest(
    string Nombre,
    string? Categoria,
    decimal Precio,
    string? Icono,
    bool Activo = true,
    string? Descripcion = null,
    string? Etiqueta = null);

/// <summary>
/// Catalogo de productos: la fuente del precio que usan el carrito y la orden. Las
/// lecturas no escriben; el alta/actualizacion la usan los scripts de prueba para
/// registrar sus SKUs de carga con un precio.
/// </summary>
public sealed class ServicioProductos
{
    private readonly IRepositorioProductos _productos;

    public ServicioProductos(IRepositorioProductos productos)
    {
        _productos = productos;
    }

    public async Task<IReadOnlyList<ProductoDto>> ListarActivosAsync(CancellationToken cancelacion)
    {
        var productos = await _productos.ListarActivosAsync(cancelacion);
        return productos.Select(ProductoDto.Desde).ToList();
    }

    /// <summary>El producto con ese SKU, o null si no existe. Un inactivo se devuelve con Activo=false.</summary>
    public async Task<ProductoDto?> ObtenerAsync(string sku, CancellationToken cancelacion)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        var producto = await _productos.ObtenerAsync(sku.Trim().ToUpperInvariant(), cancelacion);
        return producto is null ? null : ProductoDto.Desde(producto);
    }

    /// <summary>Crea el producto o reemplaza sus datos. Devuelve el producto y si era nuevo.</summary>
    public async Task<(ProductoDto Producto, bool Creado)> RegistrarAsync(
        string sku,
        RegistrarProductoRequest solicitud,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU del producto es obligatorio.", nameof(sku));
        }

        var existente = await _productos.ObtenerAsync(sku.Trim().ToUpperInvariant(), cancelacion);
        if (existente is not null)
        {
            existente.Actualizar(
                solicitud.Nombre,
                solicitud.Categoria ?? string.Empty,
                solicitud.Precio,
                solicitud.Icono ?? string.Empty,
                solicitud.Activo,
                solicitud.Descripcion,
                solicitud.Etiqueta);
            await _productos.GuardarCambiosAsync(cancelacion);
            return (ProductoDto.Desde(existente), false);
        }

        var nuevo = new Producto(
            sku,
            solicitud.Nombre,
            solicitud.Categoria ?? string.Empty,
            solicitud.Precio,
            solicitud.Icono ?? string.Empty,
            solicitud.Activo,
            solicitud.Descripcion,
            solicitud.Etiqueta);

        await _productos.AgregarAsync(nuevo, cancelacion);
        await _productos.GuardarCambiosAsync(cancelacion);
        return (ProductoDto.Desde(nuevo), true);
    }
}
