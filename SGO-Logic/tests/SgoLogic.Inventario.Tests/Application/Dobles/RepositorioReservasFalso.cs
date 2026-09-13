using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioReservas" />.</summary>
public sealed class RepositorioReservasFalso : IRepositorioReservas
{
    private readonly List<Reserva> _reservas = new();

    public int LlamadasAgregar { get; private set; }

    public IReadOnlyList<Reserva> Todas => _reservas;

    public void Precargar(Reserva reserva) => _reservas.Add(reserva);

    public Task<Reserva?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        Task.FromResult(_reservas.FirstOrDefault(reserva => reserva.OrdenId == ordenId));

    public Task<Reserva?> ObtenerPorIdAsync(string reservaId, CancellationToken cancelacion) =>
        Task.FromResult(_reservas.FirstOrDefault(reserva => reserva.Id == reservaId));

    public Task<IReadOnlyList<Reserva>> ListarActivasVencidasAsync(
        DateTime ahora,
        int limite,
        CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Reserva>>(
            _reservas
                .Where(reserva => reserva.EstaActiva && reserva.ExpiraEn <= ahora.ToUniversalTime())
                .OrderBy(reserva => reserva.ExpiraEn)
                .Take(limite)
                .ToList());

    public Task AgregarAsync(Reserva reserva, CancellationToken cancelacion)
    {
        LlamadasAgregar++;
        _reservas.Add(reserva);
        return Task.CompletedTask;
    }
}
