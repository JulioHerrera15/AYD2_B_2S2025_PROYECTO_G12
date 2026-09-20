using Microsoft.EntityFrameworkCore;
using SgoLogic.Despacho.Api.Application.Despacho;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia;

/// <summary>Implementacion de <see cref="IRepositorioDespachos" />. No llama a SaveChanges.</summary>
public sealed class RepositorioDespachos : IRepositorioDespachos
{
    private readonly DespachoDbContext _contexto;

    public RepositorioDespachos(DespachoDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<Domain.Despacho?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        _contexto.Despachos
            .Include(d => d.Lineas)
            .FirstOrDefaultAsync(d => d.OrdenId == ordenId, cancelacion);

    public async Task<IReadOnlyList<Domain.Despacho>> ListarPorUbicacionOrigenAsync(
        string ubicacionOrigenId,
        EstadoDespacho? estado,
        CancellationToken cancelacion)
    {
        var consulta = _contexto.Despachos
            .AsNoTracking()
            .Include(d => d.Lineas)
            .Where(d => d.UbicacionOrigenId == ubicacionOrigenId);

        if (estado is not null)
        {
            consulta = consulta.Where(d => d.Estado == estado);
        }

        return await consulta.OrderBy(d => d.CreadoEn).ToListAsync(cancelacion);
    }

    public async Task AgregarAsync(Domain.Despacho despacho, CancellationToken cancelacion) =>
        await _contexto.Despachos.AddAsync(despacho, cancelacion);
}
