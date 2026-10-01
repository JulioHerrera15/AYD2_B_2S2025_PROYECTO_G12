using Microsoft.EntityFrameworkCore;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia;

/// <summary>Implementacion de <see cref="IRepositorioTarjetasGuardadas" /> sobre <see cref="PagosDbContext" />.</summary>
public sealed class RepositorioTarjetasGuardadas : IRepositorioTarjetasGuardadas
{
    private readonly PagosDbContext _contexto;

    public RepositorioTarjetasGuardadas(PagosDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IReadOnlyList<TarjetaGuardada>> ListarActivasAsync(string clienteId, CancellationToken cancelacion) =>
        await _contexto.TarjetasGuardadas
            .AsNoTracking()
            .Where(tarjeta => tarjeta.ClienteId == clienteId && tarjeta.Activa)
            .OrderByDescending(tarjeta => tarjeta.UltimoUsoEn ?? tarjeta.CreadaEn)
            .ToListAsync(cancelacion);

    public Task<TarjetaGuardada?> ObtenerActivaAsync(Guid id, CancellationToken cancelacion) =>
        _contexto.TarjetasGuardadas.FirstOrDefaultAsync(tarjeta => tarjeta.Id == id && tarjeta.Activa, cancelacion);

    public Task<TarjetaGuardada?> BuscarActivaIgualAsync(
        string clienteId,
        MarcaTarjeta marca,
        string ultimos4,
        int mesExp,
        int anioExp,
        CancellationToken cancelacion) =>
        _contexto.TarjetasGuardadas.FirstOrDefaultAsync(
            tarjeta => tarjeta.Activa
                && tarjeta.ClienteId == clienteId
                && tarjeta.Marca == marca
                && tarjeta.Ultimos4 == ultimos4
                && tarjeta.MesExp == mesExp
                && tarjeta.AnioExp == anioExp,
            cancelacion);

    public async Task AgregarAsync(TarjetaGuardada tarjeta, CancellationToken cancelacion) =>
        await _contexto.TarjetasGuardadas.AddAsync(tarjeta, cancelacion);

    public Task GuardarCambiosAsync(CancellationToken cancelacion) => _contexto.SaveChangesAsync(cancelacion);
}
