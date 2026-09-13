namespace SgoLogic.Facturacion.Api.Domain;

/// <summary>
/// Una linea del documento fiscal. Hija de <see cref="DocumentoFiscal" />. Se
/// copia de la solicitud de certificacion al crear el documento y no cambia:
/// alimenta la generacion del XML.
/// </summary>
public sealed class DocumentoFiscalLinea
{
    private DocumentoFiscalLinea()
    {
        Sku = string.Empty;
        Descripcion = string.Empty;
    }

    public DocumentoFiscalLinea(
        int numeroLinea,
        string sku,
        string descripcion,
        int cantidad,
        decimal precioUnitario,
        decimal impuesto)
    {
        if (numeroLinea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numeroLinea), "El numero de linea debe ser mayor que cero.");
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        NumeroLinea = numeroLinea;
        Sku = sku;
        Descripcion = descripcion;
        Cantidad = cantidad;
        PrecioUnitario = precioUnitario;
        Impuesto = impuesto;
    }

    public Guid Id { get; private set; }

    public int NumeroLinea { get; private set; }

    public string Sku { get; private set; }

    public string Descripcion { get; private set; }

    public int Cantidad { get; private set; }

    public decimal PrecioUnitario { get; private set; }

    public decimal Impuesto { get; private set; }
}
