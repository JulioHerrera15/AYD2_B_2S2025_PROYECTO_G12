namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Una linea de una <see cref="Reserva" />: cuantas unidades de un SKU quedaron comprometidas.</summary>
public sealed class LineaReserva
{
    private LineaReserva()
    {
        Sku = string.Empty;
    }

    public LineaReserva(string sku, int cantidad)
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
        Sku = sku;
        Cantidad = cantidad;
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public int Cantidad { get; private set; }
}
