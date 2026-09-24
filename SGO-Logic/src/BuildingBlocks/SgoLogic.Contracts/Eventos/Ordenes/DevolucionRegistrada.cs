using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 11 del contrato ordenes-eventos-v1. Devolucion o cambio sobre una
/// orden entregada (CU-14, HU-014). Unico evento con aggregateType
/// "Devolucion". Dispara tres reacciones en paralelo: reingreso de inventario,
/// nota de credito y reembolso.
/// </summary>
public static class DevolucionRegistrada
{
    public const string EventType = "DevolucionRegistrada";
    public const string RoutingKey = "ordenes.devolucion.registrada";
    public const string AggregateType = "Devolucion";
}

/// <summary>Contenido de data del evento DevolucionRegistrada (seccion 7.11 del contrato).</summary>
public sealed record DevolucionRegistradaData(
    string DevolucionId,
    string OrdenOriginalId,
    string ClienteId,
    TipoDevolucion Tipo,
    bool EsParcial,
    string UbicacionReingresoId,
    IReadOnlyList<DevolucionLineaData> Lineas,
    string Moneda,
    string MontoReembolso,
    string PagoOriginalId,
    string DocumentoFiscalOriginalId,
    string RegistradaPor,
    DateTime RegistradaEn);

/// <summary>
/// Linea devuelta dentro del evento DevolucionRegistrada. UbicacionOrigenReservaId
/// y ReservaId se agregan en v1.3 del contrato para que Inventario pueda reingresar
/// la cantidad devuelta sin tener que volver a preguntarle a Ordenes de donde salio
/// la reserva original. Van al final y con valor por defecto para no romper a los
/// consumidores existentes (Pagos, Facturacion) que ya construian este record.
/// </summary>
public sealed record DevolucionLineaData(
    string Sku,
    int Cantidad,
    string Motivo,
    string? SkuReemplazo,
    string UbicacionOrigenReservaId = "",
    string ReservaId = "");
