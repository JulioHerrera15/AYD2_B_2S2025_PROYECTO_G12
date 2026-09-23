namespace SgoLogic.Contracts.Eventos.Inventario;

/// <summary>
/// Evento 5 del contrato inventario-eventos-v1. Las unidades de una devolucion
/// volvieron al stock fisico de una ubicacion (HU-014).
///
/// A diferencia de la liberacion de una reserva, aqui SI cambia la cantidad fisica:
/// una reserva es un compromiso logico, una devolucion es mercaderia que regresa al
/// anaquel. Por eso deja asiento en el ledger de movimientos (HU-016).
/// </summary>
public static class StockReingresado
{
    public const string EventType = "StockReingresado";
    public const string RoutingKey = "inventario.stock.reingresado";
    public const string AggregateType = "Devolucion";
}

public sealed record StockReingresadoData(
    string DevolucionId,
    string OrdenOriginalId,
    string UbicacionId,
    IReadOnlyList<LineaReingresadaData> Lineas,
    DateTime ReingresadoEn);

public sealed record LineaReingresadaData(string Sku, int Cantidad, int CantidadFisicaResultante);
