using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRepositorioMensajesSalientes" /> sobre
/// <see cref="OrdenesDbContext" />.
/// </summary>
public sealed class RepositorioMensajesSalientes : IRepositorioMensajesSalientes
{
    private readonly OrdenesDbContext _contexto;

    public RepositorioMensajesSalientes(OrdenesDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task AgregarAsync(MensajeSaliente mensaje, CancellationToken cancelacion)
    {
        await _contexto.MensajesSalientes.AddAsync(mensaje, cancelacion);
    }
}
