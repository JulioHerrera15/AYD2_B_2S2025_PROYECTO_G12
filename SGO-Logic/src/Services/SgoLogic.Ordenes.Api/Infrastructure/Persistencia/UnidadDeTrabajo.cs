using Microsoft.EntityFrameworkCore;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// Unidad de trabajo sobre <see cref="OrdenesDbContext" />. Un unico SaveChanges
/// abre y confirma una sola transaccion que cubre el estado de la orden, la
/// transicion y el mensaje saliente. Traduce el conflicto de bloqueo optimista a
/// <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public sealed class UnidadDeTrabajo : IUnidadDeTrabajo
{
    private readonly OrdenesDbContext _contexto;

    public UnidadDeTrabajo(OrdenesDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        try
        {
            await _contexto.SaveChangesAsync(cancelacion);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ExcepcionConflictoConcurrencia(
                "La orden fue modificada por otra operacion concurrente. Reintente con el estado actual.",
                ex);
        }
    }
}
