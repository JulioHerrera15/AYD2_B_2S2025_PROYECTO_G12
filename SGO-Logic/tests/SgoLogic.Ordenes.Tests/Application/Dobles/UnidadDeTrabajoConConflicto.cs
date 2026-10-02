using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IUnidadDeTrabajo" /> que simula que otra operacion guardo
/// primero la misma orden: cada guardado lanza <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public sealed class UnidadDeTrabajoConConflicto : IUnidadDeTrabajo
{
    public int Intentos { get; private set; }

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        Intentos++;
        throw new ExcepcionConflictoConcurrencia(
            "La orden fue modificada por otra operacion concurrente. Reintente con el estado actual.",
            new InvalidOperationException("conflicto simulado"));
    }
}
