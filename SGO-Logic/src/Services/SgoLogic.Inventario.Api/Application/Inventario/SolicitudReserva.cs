using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Entrada del caso de uso de reserva (HU-001), comun a las dos puertas: el
/// consumidor de <c>ordenes.reserva.solicitada</c> y el endpoint REST.
/// </summary>
public sealed record SolicitudReserva(
    string OrdenId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionPreferidaId,
    int TtlReservaSegundos,
    IReadOnlyList<LineaSolicitudReserva> Lineas);

/// <summary>Una linea pedida: SKU y cantidad.</summary>
public sealed record LineaSolicitudReserva(string Sku, int Cantidad);
