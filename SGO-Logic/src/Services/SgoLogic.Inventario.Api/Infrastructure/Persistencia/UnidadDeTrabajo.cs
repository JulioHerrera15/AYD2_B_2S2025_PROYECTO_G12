using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Application.Inventario;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Unidad de trabajo sobre <see cref="InventarioDbContext" />. Un unico
/// SaveChanges abre y confirma una sola transaccion que cubre las existencias
/// tocadas, la reserva y los mensajes salientes. Traduce el conflicto de bloqueo
/// optimista (columna <c>version</c> de la existencia) a
/// <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public sealed class UnidadDeTrabajo : IUnidadDeTrabajo
{
    private readonly InventarioDbContext _contexto;

    public UnidadDeTrabajo(InventarioDbContext contexto)
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
                "Una existencia fue modificada por otra operacion concurrente. Reintente con el estado actual.",
                ex);
        }
    }
}
