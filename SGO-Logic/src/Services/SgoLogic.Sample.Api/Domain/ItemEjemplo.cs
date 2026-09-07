namespace SgoLogic.Sample.Api.Domain;

/// <summary>
/// Entidad de ejemplo, sin significado de negocio, que solo demuestra el flujo
/// Controller -> Application -> Infrastructure de la plantilla.
/// </summary>
public sealed class ItemEjemplo
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public DateTime CreadoEn { get; private set; }

    private ItemEjemplo()
    {
    }

    public ItemEjemplo(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(nombre));
        }

        Id = Guid.NewGuid();
        Nombre = nombre;
        CreadoEn = DateTime.UtcNow;
    }
}