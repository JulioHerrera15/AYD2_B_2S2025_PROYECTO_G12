using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Vista de lectura de una reserva.</summary>
public sealed record ReservaDto(
    string Id,
    string OrdenId,
    string UbicacionId,
    TipoUbicacion TipoUbicacion,
    string Estado,
    int TtlSegundos,
    DateTime CreadaEn,
    DateTime ExpiraEn,
    IReadOnlyList<LineaReservaDto> Lineas)
{
    public static ReservaDto Desde(Reserva reserva) => new(
        reserva.Id,
        reserva.OrdenId,
        reserva.UbicacionId,
        reserva.TipoUbicacion,
        reserva.Estado.ToString(),
        reserva.TtlSegundos,
        reserva.CreadaEn,
        reserva.ExpiraEn,
        reserva.Lineas.Select(linea => new LineaReservaDto(linea.Sku, linea.Cantidad)).ToList());
}

public sealed record LineaReservaDto(string Sku, int Cantidad);
