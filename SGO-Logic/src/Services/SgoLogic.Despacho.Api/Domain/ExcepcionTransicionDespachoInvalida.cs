namespace SgoLogic.Despacho.Api.Domain;

/// <summary>
/// Se intento mover un despacho a un estado al que no puede llegar desde donde esta.
/// Protege contra que un estado tardio de LogiExpress pise uno mas avanzado (por
/// ejemplo un EN_TRANSITO que llega despues del ENTREGADA).
/// </summary>
public sealed class ExcepcionTransicionDespachoInvalida : Exception
{
    public ExcepcionTransicionDespachoInvalida(EstadoDespacho desde, EstadoDespacho hacia)
        : base($"Un despacho en {desde} no puede pasar a {hacia}.")
    {
        Desde = desde;
        Hacia = hacia;
    }

    public EstadoDespacho Desde { get; }

    public EstadoDespacho Hacia { get; }
}
