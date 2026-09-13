using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Puerto de persistencia del agregado <see cref="Reserva" />.</summary>
public interface IRepositorioReservas
{
    /// <summary>Trae la reserva de una orden con sus lineas cargadas, o null si no hay ninguna.</summary>
    Task<Reserva?> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion);

    /// <summary>Trae la reserva por su identificador, con sus lineas.</summary>
    Task<Reserva?> ObtenerPorIdAsync(string reservaId, CancellationToken cancelacion);

    /// <summary>
    /// Trae, con sus lineas, las reservas ACTIVA cuyo TTL ya vencio en
    /// <paramref name="ahora" />, de la mas vencida a la mas reciente y a lo sumo
    /// <paramref name="limite" />. Es el barrido autoritativo de HU-002: se apoya en
    /// el indice ix_reserva_estado_expira_en.
    /// </summary>
    Task<IReadOnlyList<Reserva>> ListarActivasVencidasAsync(
        DateTime ahora,
        int limite,
        CancellationToken cancelacion);

    /// <summary>Registra una reserva nueva en la unidad de trabajo.</summary>
    Task AgregarAsync(Reserva reserva, CancellationToken cancelacion);
}
