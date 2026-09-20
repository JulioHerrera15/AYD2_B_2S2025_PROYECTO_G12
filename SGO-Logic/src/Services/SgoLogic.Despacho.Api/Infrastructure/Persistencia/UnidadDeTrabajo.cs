using Microsoft.EntityFrameworkCore;
using SgoLogic.Despacho.Api.Application.Despacho;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia;

/// <summary>
/// Un unico SaveChanges cubre el despacho y sus filas de outbox en la misma
/// transaccion. Traduce el choque de version a <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public sealed class UnidadDeTrabajo : IUnidadDeTrabajo
{
    private readonly DespachoDbContext _contexto;

    public UnidadDeTrabajo(DespachoDbContext contexto)
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
                "El despacho fue modificado por otra operacion concurrente. Reintente con el estado actual.",
                ex);
        }
    }
}
