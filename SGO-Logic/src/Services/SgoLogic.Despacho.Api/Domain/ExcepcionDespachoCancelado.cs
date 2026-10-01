namespace SgoLogic.Despacho.Api.Domain;

/// <summary>
/// Se intento mover un despacho que fue anulado porque su orden se cancelo. Desde
/// ese estado no hay ninguna transicion. El endpoint la traduce a 409.
/// </summary>
public sealed class ExcepcionDespachoCancelado : Exception
{
    public ExcepcionDespachoCancelado(string ordenId)
        : base("El despacho fue cancelado")
    {
        OrdenId = ordenId;
    }

    public string OrdenId { get; }
}
