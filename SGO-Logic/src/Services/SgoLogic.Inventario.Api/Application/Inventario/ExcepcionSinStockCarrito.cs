namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Se lanza cuando ninguna ubicacion puede retener la cantidad pedida para el
/// carrito. El endpoint la traduce a 409 con <see cref="SinStockCarritoDto" />;
/// quien llama no debe guardar cambios.
/// </summary>
public sealed class ExcepcionSinStockCarrito : Exception
{
    public ExcepcionSinStockCarrito(string sku, int solicitado, int disponible, SucursalCercanaDto? sucursalCercana)
        : base(disponible <= 0
            ? $"No hay existencias de {sku} en ninguna sucursal."
            : $"No hay {solicitado} unidades de {sku} en una misma sucursal; como maximo hay {disponible}.")
    {
        Sku = sku;
        Solicitado = solicitado;
        Disponible = disponible;
        SucursalCercana = sucursalCercana;
    }

    public string Sku { get; }

    public int Solicitado { get; }

    public int Disponible { get; }

    public SucursalCercanaDto? SucursalCercana { get; }

    public SinStockCarritoDto ACuerpo() => new(Sku, Disponible, Message, SucursalCercana);
}
