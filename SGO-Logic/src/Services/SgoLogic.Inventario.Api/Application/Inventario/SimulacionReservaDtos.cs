using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Cuerpo de POST /api/inventario/reservas/simular. <see cref="UbicacionReferenciaId" />
/// es la sucursal del cliente (la del catalogo): contra ella se decide si el pedido
/// sale de "otra" tienda. <see cref="CarritoId" /> hace que las retenciones activas
/// de ese carrito cuenten como disponibles.
/// </summary>
public sealed record SimularReservaRequest(
    string? CarritoId,
    MetodoEntrega MetodoEntrega,
    string? UbicacionPreferidaId,
    string? UbicacionReferenciaId,
    IReadOnlyList<LineaSolicitudReserva> Lineas);

/// <summary>
/// Resultado de la simulacion: desde donde saldria el pedido si se reservara ahora.
/// Con recogida en tienda, <see cref="Factible" /> es false si la tienda elegida no
/// cubre todo; en ese caso <see cref="UbicacionId" /> es la tienda sugerida que si.
/// <see cref="UbicacionId" /> es null solo cuando ninguna ubicacion cubre el pedido.
/// </summary>
public sealed record SimulacionReservaDto(
    bool Factible,
    string? UbicacionId,
    string? NombreUbicacion,
    TipoUbicacion? TipoUbicacion,
    bool EsOtraSucursal,
    string? Motivo);
