using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Salida del caso de uso de reserva. Distingue tres desenlaces:
/// <list type="bullet">
///   <item>reserva creada (<see cref="Reserva" /> con <see cref="YaExistia" /> false);</item>
///   <item>reserva ya existente para la orden, se devuelve tal cual (idempotencia);</item>
///   <item>rechazo de negocio por falta de stock (<see cref="Rechazada" /> true).</item>
/// </list>
/// </summary>
public sealed record ResultadoReservaDto(
    ReservaDto? Reserva,
    bool YaExistia,
    bool Rechazada,
    MotivoRechazoReserva? MotivoRechazo,
    IReadOnlyList<LineaFaltanteDto> LineasFaltantes)
{
    public static ResultadoReservaDto Confirmada(ReservaDto reserva) =>
        new(reserva, YaExistia: false, Rechazada: false, MotivoRechazo: null, Array.Empty<LineaFaltanteDto>());

    public static ResultadoReservaDto YaReservada(ReservaDto reserva) =>
        new(reserva, YaExistia: true, Rechazada: false, MotivoRechazo: null, Array.Empty<LineaFaltanteDto>());

    public static ResultadoReservaDto RechazadaPorStock(
        MotivoRechazoReserva motivo,
        IReadOnlyList<LineaFaltanteDto> lineasFaltantes) =>
        new(Reserva: null, YaExistia: false, Rechazada: true, motivo, lineasFaltantes);
}

public sealed record LineaFaltanteDto(string Sku, int CantidadSolicitada, int CantidadDisponible);
