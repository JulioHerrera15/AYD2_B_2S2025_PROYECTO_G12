using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Se lanza cuando se intenta una transicion que la maquina de estados no permite.
/// Es una violacion de invariante del dominio, no un error de entrada del usuario.
/// </summary>
public sealed class ExcepcionTransicionInvalida : InvalidOperationException
{
    public ExcepcionTransicionInvalida(EstadoOrden desde, EstadoOrden hacia)
        : base($"Transicion invalida de {desde} a {hacia}.")
    {
        Desde = desde;
        Hacia = hacia;
    }

    public EstadoOrden Desde { get; }

    public EstadoOrden Hacia { get; }
}
