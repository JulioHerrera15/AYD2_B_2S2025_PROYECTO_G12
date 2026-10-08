namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Reembolso a la tarjeta de una orden cancelada despues de pagarse. NO_APLICA en toda
/// orden que no lo requiere (sin cobro o a credito); PENDIENTE desde la cancelacion hasta
/// que Pagos informa el resultado (ReembolsoCancelacionAplicado o ReembolsoCancelacionFallido).
/// </summary>
public enum EstadoReembolsoOrden
{
    NO_APLICA,
    PENDIENTE,
    APLICADO,
    FALLIDO
}
