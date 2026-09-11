using SgoLogic.Inventario.Api.Application.Inventario;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IUnidadDeTrabajo" />. Cuenta las confirmaciones y, si se le
/// arma, lanza <see cref="ExcepcionConflictoConcurrencia" /> para simular el
/// choque del bloqueo optimista.
/// </summary>
public sealed class UnidadDeTrabajoFalsa : IUnidadDeTrabajo
{
    public int Confirmaciones { get; private set; }

    public bool LanzarConflicto { get; set; }

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        if (LanzarConflicto)
        {
            throw new ExcepcionConflictoConcurrencia("conflicto simulado", new InvalidOperationException());
        }

        Confirmaciones++;
        return Task.CompletedTask;
    }
}
