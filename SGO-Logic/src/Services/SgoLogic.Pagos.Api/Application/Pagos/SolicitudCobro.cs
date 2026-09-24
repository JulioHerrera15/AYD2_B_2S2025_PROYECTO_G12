using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Datos necesarios para procesar un cobro. Se arma desde el evento
/// <c>CobroSolicitado</c> en el consumidor, y desde el <see cref="Domain.Pago" />
/// persistido en el endpoint de reintento.
/// </summary>
public sealed record SolicitudCobro(
    string OrdenId,
    string ClienteId,
    Canal Canal,
    Moneda Moneda,
    decimal Monto,
    MedioPago MedioPago,
    string? TokenMedioPago,
    string? UltimosCuatro,
    string? Marca);
