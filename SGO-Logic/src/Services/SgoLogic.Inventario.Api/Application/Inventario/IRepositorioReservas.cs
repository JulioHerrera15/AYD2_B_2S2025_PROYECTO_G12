using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Puerto de persistencia del agregado <see cref="Reserva" />.</summary>
public interface IRepositorioReservas
{
    /// <summary>Trae la reserva de una orden con sus lineas cargadas, o null si no hay ninguna.</summary>
    Task<Reserva?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion);

    /// <summary>Trae la reserva por su identificador, con sus lineas.</summary>
    Task<Reserva?> ObtenerPorIdAsync(string reservaId, CancellationToken cancelacion);

    /// <summary>Registra una reserva nueva en la unidad de trabajo.</summary>
    Task AgregarAsync(Reserva reserva, CancellationToken cancelacion);
}
