using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioSucursales" />.</summary>
public sealed class RepositorioSucursalesFalso : IRepositorioSucursales
{
    private readonly List<Sucursal> _sucursales = new();

    public RepositorioSucursalesFalso Precargar(params Sucursal[] sucursales)
    {
        _sucursales.AddRange(sucursales);
        return this;
    }

    public Task<IReadOnlyList<Sucursal>> ListarActivasAsync(CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Sucursal>>(
            _sucursales
                .Where(sucursal => sucursal.Activa)
                .OrderBy(sucursal => sucursal.TipoUbicacion)
                .ThenBy(sucursal => sucursal.Id)
                .ToList());
}
