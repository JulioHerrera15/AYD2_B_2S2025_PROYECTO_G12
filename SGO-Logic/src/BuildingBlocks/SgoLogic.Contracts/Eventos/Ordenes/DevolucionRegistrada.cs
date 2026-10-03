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

/// <summary>
/// Contenido de data del evento DevolucionRegistrada (seccion 7.11 del contrato).
/// MedioPago (v1.4) es el de la orden original: con CREDITO la devolucion la aplica
/// Credito y Pagos la ignora. Opcional y al final para no romper a los consumidores
/// existentes; null se trata como un pago con tarjeta, igual que antes.
/// </summary>
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
    DateTime RegistradaEn,
    MedioPago? MedioPago = null);

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
