namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Se lanza cuando una linea pide mas unidades de las disponibles en la existencia
/// candidata. El servicio de reservas la traduce a un rechazo de negocio
/// (evento ReservaInventarioRechazada / 409 en el endpoint), nunca a un 500.
/// </summary>
public sealed class ExcepcionStockInsuficiente : Exception
{
    public ExcepcionStockInsuficiente(string sku, int solicitado, int disponible)
        : base($"Stock insuficiente para el SKU {sku}: se pidieron {solicitado} y hay {disponible} disponibles.")
    {
        Sku = sku;
        Solicitado = solicitado;
        Disponible = disponible;
    }

    public string Sku { get; }

    public int Solicitado { get; }

    public int Disponible { get; }
}
