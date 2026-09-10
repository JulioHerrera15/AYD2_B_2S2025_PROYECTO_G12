using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>Proyeccion de salida de un pago con todos sus intentos. Nunca expone el token del medio de pago.</summary>
public sealed record PagoDto(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    EstadoPago Estado,
    int Secuencia,
    DateTime CreadoEn,
    DateTime ActualizadoEn,
    IReadOnlyList<IntentoPagoDto> Intentos)
{
    public static PagoDto Desde(Pago pago) => new(
        pago.OrdenId,
        pago.ClienteId,
        pago.Canal,
        pago.Moneda,
        pago.Monto,
        pago.MedioPago,
        pago.Estado,
        pago.Secuencia,
        pago.CreadoEn,
        pago.ActualizadoEn,
        pago.Intentos
            .OrderBy(intento => intento.IntentoNumero)
            .Select(IntentoPagoDto.Desde)
            .ToList());
}

/// <summary>
/// Proyeccion de salida de un intento de cobro. No incluye el token del medio de
/// pago (solo ultimos cuatro y marca) ni jamas datos de tarjeta.
/// </summary>
public sealed record IntentoPagoDto(
    int IntentoNumero,
    MedioPago MedioPago,
    string? UltimosCuatro,
    string? Marca,
    decimal Monto,
    Moneda Moneda,
    EstadoIntentoPago Estado,
    string? CodigoRespuesta,
    string? MensajeRespuesta,
    int? LatenciaMs,
    string? AutorizacionId,
    DateTime IniciadoEn,
    DateTime? FinalizadoEn)
{
    public static IntentoPagoDto Desde(IntentoPago intento) => new(
        intento.IntentoNumero,
        intento.MedioPago,
        intento.UltimosCuatro,
        intento.Marca,
        intento.Monto,
        intento.Moneda,
        intento.Estado,
        intento.CodigoRespuesta,
        intento.MensajeRespuesta,
        intento.LatenciaMs,
        intento.AutorizacionId,
        intento.IniciadoEn,
        intento.FinalizadoEn);
}
