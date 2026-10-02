using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="ICatalogoProductos" />. Trae por defecto el SKU que usan las
/// pruebas de creacion de orden (SKU-LB-00871 a 1250.00) y se puede armar con otros
/// precios o para simular que Inventario no responde.
/// </summary>
public sealed class CatalogoProductosFalso : ICatalogoProductos
{
    public Dictionary<string, ProductoCatalogo> Productos { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SKU-LB-00871"] = new ProductoCatalogo("SKU-LB-00871", 1250.00m, Activo: true)
    };

    public bool Caido { get; set; }

    public List<string> Consultas { get; } = new();

    public CatalogoProductosFalso ConPrecio(string sku, decimal precio, bool activo = true)
    {
        Productos[sku] = new ProductoCatalogo(sku, precio, activo);
        return this;
    }

    public Task<ProductoCatalogo?> ObtenerAsync(string sku, CancellationToken cancelacion)
    {
        Consultas.Add(sku);

        if (Caido)
        {
            throw new ExcepcionCatalogoNoDisponible("Inventario no respondio (simulado).");
        }

        return Task.FromResult(Productos.TryGetValue(sku, out var producto) ? producto : null);
    }
}
