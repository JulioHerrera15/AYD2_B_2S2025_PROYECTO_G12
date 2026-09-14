using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Una ubicacion conocida por Inventario, con el resumen de lo que guarda. Las
/// ubicaciones se derivan de las existencias cargadas (DISTINCT sobre ubicacion_id
/// y tipo_ubicacion): no hay tabla propia de ubicaciones porque Inventario no
/// necesita metadata rica de ellas todavia.
/// </summary>
public sealed record UbicacionResumenDto(
    string UbicacionId,
    TipoUbicacion TipoUbicacion,
    int CantidadDeSkus,
    int CantidadTotalFisica,
    int CantidadTotalReservada,
    int TotalDisponible);
