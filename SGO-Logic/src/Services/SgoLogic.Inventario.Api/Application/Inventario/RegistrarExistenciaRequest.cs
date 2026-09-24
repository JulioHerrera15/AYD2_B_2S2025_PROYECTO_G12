using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Alta o ajuste de una existencia por ubicacion. Es la carga de datos base de
/// Inventario (parte de HU-016); HU-001 la usa para poder reservar contra algo.
/// </summary>
public sealed record RegistrarExistenciaRequest(
    string Sku,
    string UbicacionId,
    TipoUbicacion TipoUbicacion,
    int CantidadFisica);
