using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioReservasCarrito" />.</summary>
public sealed class RepositorioReservasCarritoFalso : IRepositorioReservasCarrito
{
    private readonly List<ReservaCarrito> _retenciones = new();

    public int LlamadasAgregar { get; private set; }

    public IReadOnlyList<ReservaCarrito> Todas => _retenciones;

    public RepositorioReservasCarritoFalso Precargar(params ReservaCarrito[] retenciones)
    {
        _retenciones.AddRange(retenciones);
        return this;
    }

    public Task<IReadOnlyList<ReservaCarrito>> ListarVigentesPorCarritoAsync(string carritoId, CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<ReservaCarrito>>(
            _retenciones
                .Where(retencion => retencion.CarritoId == carritoId && retencion.EstaVigente)
                .OrderBy(retencion => retencion.CreadaEn)
                .ToList());

    public Task<IReadOnlyList<ReservaCarrito>> ListarActivasVencidasAsync(DateTime ahora, int limite, CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<ReservaCarrito>>(
            _retenciones
                .Where(retencion => retencion.EstaActiva && retencion.ExpiraEn <= ahora.ToUniversalTime())
                .OrderBy(retencion => retencion.ExpiraEn)
                .Take(limite)
                .ToList());

    public Task<ReservaCarrito?> ObtenerPorIdAsync(Guid id, CancellationToken cancelacion) =>
        Task.FromResult(_retenciones.FirstOrDefault(retencion => retencion.Id == id));

    public Task AgregarAsync(ReservaCarrito retencion, CancellationToken cancelacion)
    {
        LlamadasAgregar++;
        _retenciones.Add(retencion);
        return Task.CompletedTask;
    }
}
