namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Se lanza cuando se pide operar sobre una orden que no existe. El endpoint la
/// traduce a 404.
/// </summary>
public sealed class ExcepcionOrdenNoEncontrada : Exception
{
    public ExcepcionOrdenNoEncontrada(string ordenId)
        : base($"No existe la orden {ordenId}.")
    {
        OrdenId = ordenId;
    }

    public string OrdenId { get; }
}
