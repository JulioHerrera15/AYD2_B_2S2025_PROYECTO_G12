namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Se lanza cuando un cliente intenta operar sobre una orden que no es suya. El
/// endpoint la traduce a 403.
/// </summary>
public sealed class ExcepcionOrdenAjena : Exception
{
    public ExcepcionOrdenAjena(string ordenId)
        : base("La orden no pertenece al cliente")
    {
        OrdenId = ordenId;
    }

    public string OrdenId { get; }
}
