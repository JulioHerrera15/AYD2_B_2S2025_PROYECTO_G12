using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Datos de entrada para crear una orden en el estado inicial CREADA. DTO propio
/// del borde HTTP: nunca se exponen las entidades del dominio.
/// </summary>
public sealed record CrearOrdenRequest(
    string NumeroOrden,
    string ClienteId,
    Canal Canal,
    MetodoEntrega MetodoEntrega,
    Moneda Moneda,
    decimal Impuestos,
    IReadOnlyList<CrearLineaOrdenRequest> Lineas,
    string? Id = null,
    string? UbicacionSolicitadaId = null);

public sealed record CrearLineaOrdenRequest(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario);
