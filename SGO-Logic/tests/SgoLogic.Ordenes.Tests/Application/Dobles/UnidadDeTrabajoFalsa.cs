using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IUnidadDeTrabajo" />. Solo cuenta cuantas veces se pidio
/// confirmar la transaccion; nunca toca una base real.
/// </summary>
public sealed class UnidadDeTrabajoFalsa : IUnidadDeTrabajo
{
    public int Confirmaciones { get; private set; }

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        Confirmaciones++;
        return Task.CompletedTask;
    }
}
