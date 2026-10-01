using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Cuerpo del endpoint REST de reserva. Se mapea a <see cref="SolicitudReserva" />.</summary>
public sealed record CrearReservaRequest(
    string OrdenId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionPreferidaId,
    int TtlReservaSegundos,
    IReadOnlyList<LineaSolicitudReserva> Lineas,
    string? CarritoId = null)
{
    public SolicitudReserva ASolicitud() => new(
        OrdenId,
        MetodoEntrega,
        UbicacionPreferidaId,
        TtlReservaSegundos,
        Lineas ?? Array.Empty<LineaSolicitudReserva>(),
        CarritoId);
}
