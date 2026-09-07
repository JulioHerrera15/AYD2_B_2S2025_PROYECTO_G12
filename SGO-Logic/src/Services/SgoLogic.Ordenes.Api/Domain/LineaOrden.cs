namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Linea de una orden. El precio viaja congelado al momento de la compra: el
/// Catalogo Comercial es dependencia externa y no es fuente de verdad despues.
/// </summary>
public sealed class LineaOrden
{
    private LineaOrden()
    {
        Sku = string.Empty;
        Descripcion = string.Empty;
    }

    public LineaOrden(string sku, string descripcion, int cantidad, decimal precioUnitario)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            throw new ArgumentException("La descripcion es obligatoria.", nameof(descripcion));
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        }

        if (precioUnitario < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precioUnitario), "El precio no puede ser negativo.");
        }

        Id = Guid.NewGuid();
        Sku = sku;
        Descripcion = descripcion;
        Cantidad = cantidad;
        PrecioUnitario = precioUnitario;
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public string Descripcion { get; private set; }

    public int Cantidad { get; private set; }

    public decimal PrecioUnitario { get; private set; }

    public decimal Subtotal => PrecioUnitario * Cantidad;
}
