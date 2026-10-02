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

/// <summary>
/// Informacion publica de un enlace de reintento para mostrar al cliente antes de
/// ejecutar el cobro. <see cref="DatosRequeridos" /> dice que tiene que volver a
/// escribir el cliente: NINGUNO (el token sigue vigente), CVV (el token vencio pero
/// era de una tarjeta guardada activa) o TARJETA (el formulario completo).
/// </summary>
public sealed record EnlaceReintentoInfoDto(
    string Token,
    string OrdenId,
    decimal Monto,
    string Moneda,
    bool EstaVigente,
    bool Usado,
    DateTime ExpiraEn,
    string EstadoPago,
    string? UltimosCuatro,
    string? Marca,
    string? ClienteId,
    string DatosRequeridos = DatosRequeridosReintento.Ninguno);

/// <summary>Valores de <see cref="EnlaceReintentoInfoDto.DatosRequeridos" />.</summary>
public static class DatosRequeridosReintento
{
    public const string Ninguno = "NINGUNO";
    public const string Cvv = "CVV";
    public const string Tarjeta = "TARJETA";
}

/// <summary>
/// Cuerpo opcional de POST /api/pagos/reintentos/{token}. Sin cuerpo se reintenta con
/// el token del ultimo intento. <see cref="TokenMedioPago" /> trae un token nuevo de
/// POST /api/pagos/tokens (tarjeta completa); <see cref="Cvv" /> pide uno nuevo desde
/// la misma tarjeta guardada. <see cref="ToString" /> no imprime el CVV.
/// </summary>
public sealed record ReintentoPagoRequest(string? TokenMedioPago, string? Cvv)
{
    public override string ToString() => nameof(ReintentoPagoRequest) + " { datos omitidos }";
}
