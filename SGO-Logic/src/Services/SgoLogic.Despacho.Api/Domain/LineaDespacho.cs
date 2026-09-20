namespace SgoLogic.Despacho.Api.Domain;

/// <summary>Un SKU y su cantidad dentro de un despacho. Entidad hija, sin identidad propia de negocio.</summary>
public sealed class LineaDespacho
{
    private LineaDespacho()
    {
        DespachoId = string.Empty;
        Sku = string.Empty;
        Descripcion = string.Empty;
    }

    public LineaDespacho(string sku, string descripcion, int cantidad)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        DespachoId = string.Empty;
        Sku = sku;
        Descripcion = descripcion ?? string.Empty;
        Cantidad = cantidad;
    }

    public Guid Id { get; private set; }

    public string DespachoId { get; private set; }

    public string Sku { get; private set; }

    public string Descripcion { get; private set; }

    public int Cantidad { get; private set; }
}
