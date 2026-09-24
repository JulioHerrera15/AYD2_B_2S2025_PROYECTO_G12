using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Vista de lectura de una existencia por ubicacion.</summary>
public sealed record ExistenciaDto(
    string Sku,
    string UbicacionId,
    TipoUbicacion TipoUbicacion,
    int CantidadFisica,
    int CantidadReservada,
    int Disponible,
    int Version)
{
    public static ExistenciaDto Desde(Existencia existencia) => new(
        existencia.Sku,
        existencia.UbicacionId,
        existencia.TipoUbicacion,
        existencia.CantidadFisica,
        existencia.CantidadReservada,
        existencia.Disponible,
        existencia.Version);
}
