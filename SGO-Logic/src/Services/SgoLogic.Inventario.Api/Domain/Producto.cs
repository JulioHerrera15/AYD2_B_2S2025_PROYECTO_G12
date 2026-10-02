namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Producto del catalogo con su precio vigente. Es la fuente de verdad del precio:
/// el carrito (Auth) y la creacion de la orden (Ordenes) lo toman de aqui en lugar
/// de confiar en lo que manda el navegador. <see cref="Sku" /> es el mismo
/// identificador de <see cref="Existencia.Sku" />.
///
/// <see cref="Descripcion" /> y <see cref="Etiqueta" /> son solo de presentacion (la
/// tarjeta del catalogo de la Web); no intervienen en ninguna regla.
/// </summary>
public sealed class Producto
{
    public const int LargoMaximoSku = 40;
    public const int LargoMaximoNombre = 160;
    public const int LargoMaximoCategoria = 60;
    public const int LargoMaximoIcono = 60;
    public const int LargoMaximoDescripcion = 400;
    public const int LargoMaximoEtiqueta = 40;

    private Producto()
    {
        Sku = string.Empty;
        Nombre = string.Empty;
        Categoria = string.Empty;
        Icono = string.Empty;
    }

    public Producto(
        string sku,
        string nombre,
        string categoria,
        decimal precio,
        string icono,
        bool activo = true,
        string? descripcion = null,
        string? etiqueta = null)
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Trim().Length > LargoMaximoSku)
        {
            throw new ArgumentException("El SKU del producto es obligatorio.", nameof(sku));
        }

        Sku = sku.Trim().ToUpperInvariant();
        Nombre = string.Empty;
        Categoria = string.Empty;
        Icono = string.Empty;
        Actualizar(nombre, categoria, precio, icono, activo, descripcion, etiqueta);
    }

    public string Sku { get; private set; }

    public string Nombre { get; private set; }

    public string Categoria { get; private set; }

    /// <summary>Precio unitario en GTQ con dos decimales.</summary>
    public decimal Precio { get; private set; }

    /// <summary>Nombre del icono de Material Symbols que usa la Web.</summary>
    public string Icono { get; private set; }

    public bool Activo { get; private set; }

    public string? Descripcion { get; private set; }

    public string? Etiqueta { get; private set; }

    /// <summary>Reemplaza los datos del producto (upsert del catalogo).</summary>
    public void Actualizar(
        string nombre,
        string categoria,
        decimal precio,
        string icono,
        bool activo,
        string? descripcion,
        string? etiqueta)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > LargoMaximoNombre)
        {
            throw new ArgumentException("El nombre del producto es obligatorio.", nameof(nombre));
        }

        if (precio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(precio), "El precio del producto debe ser mayor que cero.");
        }

        if (decimal.Round(precio, 2) != precio)
        {
            throw new ArgumentOutOfRangeException(nameof(precio), "El precio del producto admite a lo sumo dos decimales.");
        }

        Nombre = nombre.Trim();
        Categoria = Recortar(categoria, LargoMaximoCategoria) ?? string.Empty;
        Precio = precio;
        Icono = Recortar(icono, LargoMaximoIcono) ?? string.Empty;
        Activo = activo;
        Descripcion = Recortar(descripcion, LargoMaximoDescripcion);
        Etiqueta = Recortar(etiqueta, LargoMaximoEtiqueta);
    }

    private static string? Recortar(string? valor, int largo)
    {
        var limpio = valor?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            return null;
        }

        return limpio.Length > largo ? limpio[..largo] : limpio;
    }
}
