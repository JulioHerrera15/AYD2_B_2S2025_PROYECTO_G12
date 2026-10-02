using Microsoft.EntityFrameworkCore;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion EF de <see cref="IRepositorioCredito" />. Comparte el
/// <see cref="CreditoDbContext" /> scoped con IOutboxWriter, asi que el
/// GuardarCambiosAsync confirma el evento junto con el cambio de negocio.
/// </summary>
public sealed class RepositorioCredito : IRepositorioCredito
{
    private readonly CreditoDbContext _contexto;

    public RepositorioCredito(CreditoDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<HistorialCliente?> ObtenerHistorialAsync(string clienteId, CancellationToken cancelacion) =>
        _contexto.Historiales.FirstOrDefaultAsync(historial => historial.ClienteId == clienteId, cancelacion);

    public Task<PerfilCrediticio?> ObtenerPerfilAsync(string clienteId, CancellationToken cancelacion) =>
        _contexto.Perfiles.FirstOrDefaultAsync(perfil => perfil.ClienteId == clienteId, cancelacion);

    public Task<LineaCredito?> ObtenerLineaAsync(string clienteId, CancellationToken cancelacion) =>
        _contexto.Lineas.FirstOrDefaultAsync(linea => linea.ClienteId == clienteId, cancelacion);

    public void AgregarHistorial(HistorialCliente historial) => _contexto.Historiales.Add(historial);

    public void AgregarPerfil(PerfilCrediticio perfil) => _contexto.Perfiles.Add(perfil);

    public void AgregarLinea(LineaCredito linea) => _contexto.Lineas.Add(linea);

    public Task<bool> HayHistorialesAsync(CancellationToken cancelacion) =>
        _contexto.Historiales.AnyAsync(cancelacion);

    public Task GuardarCambiosAsync(CancellationToken cancelacion) =>
        _contexto.SaveChangesAsync(cancelacion);
}
