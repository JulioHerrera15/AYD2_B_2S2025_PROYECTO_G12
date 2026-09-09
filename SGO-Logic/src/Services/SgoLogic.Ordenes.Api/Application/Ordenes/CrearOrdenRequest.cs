using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Datos de entrada para crear una orden en el estado inicial CREADA. DTO propio
/// del borde HTTP: nunca se exponen las entidades del dominio.
///
/// El cliente no elige el identificador ni el numero de orden: los genera el
/// servidor a partir de la secuencia <c>ordenes.orden_correlativo</c> y respetan
/// el formato ORD-&lt;anio&gt;-&lt;correlativo de 6 digitos&gt; que exige
/// ordenes-eventos-v1.
/// </summary>
public sealed record CrearOrdenRequest(
    string ClienteId,
    Canal Canal,
    MetodoEntrega MetodoEntrega,
    Moneda Moneda,
    MedioPago MedioPago,
    decimal Impuestos,
    IReadOnlyList<CrearLineaOrdenRequest> Lineas,
    string? UbicacionSolicitadaId = null);

public sealed record CrearLineaOrdenRequest(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario);
