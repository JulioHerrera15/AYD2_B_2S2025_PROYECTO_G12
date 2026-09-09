using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Entrada de <c>POST /api/ordenes/{id}/metodo-entrega</c> (EV-31, CUN-01.5). El
/// metodo de entrega de una orden puede cambiar despues de confirmada; no es una
/// transicion de estado. La direccion es obligatoria si el nuevo metodo es
/// domicilio; la ubicacion de recogida, si es recogida en tienda.
/// </summary>
public sealed record CambiarMetodoEntregaRequest(
    MetodoEntrega MetodoEntregaNuevo,
    SolicitanteCambio SolicitadoPor,
    string? UbicacionRecogidaId = null,
    DireccionEntregaData? DireccionEntrega = null);
