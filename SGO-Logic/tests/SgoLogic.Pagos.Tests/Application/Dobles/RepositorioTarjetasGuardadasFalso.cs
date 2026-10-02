using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioTarjetasGuardadas" />.</summary>
public sealed class RepositorioTarjetasGuardadasFalso : IRepositorioTarjetasGuardadas
{
    private readonly List<TarjetaGuardada> _tarjetas = new();

    public IReadOnlyList<TarjetaGuardada> Todas => _tarjetas;

    public int Guardados { get; private set; }

    public RepositorioTarjetasGuardadasFalso Precargar(params TarjetaGuardada[] tarjetas)
    {
        _tarjetas.AddRange(tarjetas);
        return this;
    }

    public Task<IReadOnlyList<TarjetaGuardada>> ListarActivasAsync(string clienteId, CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<TarjetaGuardada>>(_tarjetas
            .Where(t => t.Activa && t.ClienteId == clienteId)
            .OrderByDescending(t => t.UltimoUsoEn ?? t.CreadaEn)
            .ToList());

    public Task<TarjetaGuardada?> ObtenerActivaAsync(Guid id, CancellationToken cancelacion) =>
        Task.FromResult(_tarjetas.FirstOrDefault(t => t.Id == id && t.Activa));

    public Task<TarjetaGuardada?> BuscarActivaIgualAsync(
        string clienteId,
        MarcaTarjeta marca,
        string ultimos4,
        int mesExp,
        int anioExp,
        CancellationToken cancelacion) =>
        Task.FromResult(_tarjetas.FirstOrDefault(t =>
            t.Activa && t.ClienteId == clienteId && t.Marca == marca
            && t.Ultimos4 == ultimos4 && t.MesExp == mesExp && t.AnioExp == anioExp));

    public Task AgregarAsync(TarjetaGuardada tarjeta, CancellationToken cancelacion)
    {
        _tarjetas.Add(tarjeta);
        return Task.CompletedTask;
    }

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        Guardados++;
        return Task.CompletedTask;
    }
}
