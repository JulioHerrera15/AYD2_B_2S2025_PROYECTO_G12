using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Vista de lectura de un asiento del ledger de movimientos.</summary>
public sealed record MovimientoExistenciaDto(
    Guid Id,
    string Sku,
    string UbicacionId,
    string Tipo,
    int Cantidad,
    int CantidadFisicaResultante,
    string Motivo,
    DateTime RegistradoEn)
{
    public static MovimientoExistenciaDto Desde(MovimientoExistencia movimiento) => new(
        movimiento.Id,
        movimiento.Sku,
        movimiento.UbicacionId,
        movimiento.Tipo.ToString(),
        movimiento.Cantidad,
        movimiento.CantidadFisicaResultante,
        movimiento.Motivo.ToString(),
        movimiento.RegistradoEn);
}
