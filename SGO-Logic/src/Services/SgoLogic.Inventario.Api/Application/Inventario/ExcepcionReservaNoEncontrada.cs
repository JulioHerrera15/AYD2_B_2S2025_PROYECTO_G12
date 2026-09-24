namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Se lanza al consultar la reserva de una orden que no tiene ninguna. El endpoint la traduce a 404.</summary>
public sealed class ExcepcionReservaNoEncontrada : Exception
{
    public ExcepcionReservaNoEncontrada(string ordenId)
        : base($"No existe una reserva para la orden {ordenId}.")
    {
        OrdenId = ordenId;
    }

    public string OrdenId { get; }
}
