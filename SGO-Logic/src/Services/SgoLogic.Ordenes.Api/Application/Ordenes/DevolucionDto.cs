using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Proyeccion de salida de una devolucion. Se construye desde el agregado con
/// <see cref="Desde" />; las entidades del dominio no cruzan el borde HTTP.
/// </summary>
public sealed record DevolucionDto(
    string Id,
    string OrdenOriginalId,
    string ClienteId,
    TipoDevolucion Tipo,
    bool EsParcial,
    string UbicacionReingresoId,
    decimal MontoReembolso,
    string Moneda,
    EstadoDevolucion Estado,
    string RegistradaPor,
    DateTime RegistradaEn,
    IReadOnlyList<DevolucionLineaDto> Lineas)
{
    public static DevolucionDto Desde(Devolucion devolucion) => new(
        devolucion.Id,
        devolucion.OrdenOriginalId,
        devolucion.ClienteId,
        devolucion.Tipo,
        devolucion.EsParcial,
        devolucion.UbicacionReingresoId,
        devolucion.MontoReembolso,
        devolucion.Moneda,
        devolucion.Estado,
        devolucion.RegistradaPor,
        devolucion.RegistradaEn,
        devolucion.Lineas
            .Select(l => new DevolucionLineaDto(l.Sku, l.Cantidad, l.Motivo, l.SkuReemplazo, l.PrecioUnitario, l.Subtotal))
            .ToList());
}

/// <summary>Linea individual de una devolucion en la respuesta.</summary>
public sealed record DevolucionLineaDto(
    string Sku,
    int Cantidad,
    string Motivo,
    string? SkuReemplazo,
    decimal PrecioUnitario,
    decimal Subtotal);
