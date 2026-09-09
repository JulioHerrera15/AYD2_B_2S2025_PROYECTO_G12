using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 5 del contrato ordenes-eventos-v1. Ordenes solicita el cobro a Pagos
/// (CU-03). Se emite al pasar la orden a COBRO_SOLICITADO.
///
/// Restriccion dura (RES-08, EV-25, ADR-010): este payload no contiene ni
/// contendra PAN, CVV ni fecha de vencimiento completa. Solo token, ultimos cuatro
/// digitos y marca.
/// </summary>
public static class CobroSolicitado
{
    public const string EventType = "CobroSolicitado";
    public const string RoutingKey = "ordenes.cobro.solicitado";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento CobroSolicitado (seccion 7.5 del contrato).</summary>
public sealed record CobroSolicitadoData(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    string? TokenMedioPago,
    string? UltimosCuatro,
    string? Marca,
    int IntentoNumero,
    DateTime SolicitadoEn);
