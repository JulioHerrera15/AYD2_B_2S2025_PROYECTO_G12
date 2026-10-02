using Microsoft.EntityFrameworkCore;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia;

/// <summary>Implementacion de <see cref="IRepositorioTokensPago" /> sobre <see cref="PagosDbContext" />.</summary>
public sealed class RepositorioTokensPago : IRepositorioTokensPago
{
    private readonly PagosDbContext _contexto;

    public RepositorioTokensPago(PagosDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<TokenPago?> ObtenerAsync(string id, CancellationToken cancelacion) =>
        _contexto.TokensPago.FirstOrDefaultAsync(token => token.Id == id, cancelacion);

    public async Task AgregarAsync(TokenPago token, CancellationToken cancelacion) =>
        await _contexto.TokensPago.AddAsync(token, cancelacion);

    public async Task<IReadOnlyList<TokenPago>> ListarActivosVencidosAsync(
        DateTime ahora,
        int maximo,
        CancellationToken cancelacion) =>
        await _contexto.TokensPago
            .Where(token => token.Estado == EstadoTokenPago.ACTIVO && token.ExpiraEn <= ahora)
            .OrderBy(token => token.ExpiraEn)
            .Take(maximo)
            .ToListAsync(cancelacion);

    public Task GuardarCambiosAsync(CancellationToken cancelacion) => _contexto.SaveChangesAsync(cancelacion);
}
