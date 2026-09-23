using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Inventario;

/// <summary>
/// Evento 4 del contrato inventario-eventos-v1. La reserva se libero por
/// compensacion de la saga (HU-003): un cobro rechazado o una cancelacion pidieron
/// devolver las unidades antes de que venciera el TTL.
///
/// Se distingue de <see cref="ReservaInventarioExpirada" /> a proposito: alla el
/// disparador es el reloj y aca una decision de negocio, y Consulta de Estado
/// necesita poder contarle al cliente cual de las dos paso.
/// </summary>
public static class ReservaInventarioLiberada
{
    public const string EventType = "ReservaInventarioLiberada";
    public const string RoutingKey = "inventario.reserva.liberada";
    public const string AggregateType = "Reserva";
}

public sealed record ReservaInventarioLiberadaData(
    string ReservaId,
    string OrdenId,
    string UbicacionId,
    MotivoLiberacionReserva Motivo,
    IReadOnlyList<ReservaConfirmadaLineaData> Lineas,
    DateTime LiberadaEn);
