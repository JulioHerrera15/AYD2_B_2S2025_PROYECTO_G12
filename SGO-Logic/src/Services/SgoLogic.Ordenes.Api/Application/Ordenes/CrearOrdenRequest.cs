using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Datos de entrada para crear una orden en el estado inicial CREADA. DTO propio
/// del borde HTTP: nunca se exponen las entidades del dominio.
///
/// El cliente no elige el identificador ni el numero de orden: los genera el
/// servidor a partir de la secuencia <c>ordenes.orden_correlativo</c> y respetan
/// el formato ORD-<anio>-<correlativo de 6 digitos> que exige
/// ordenes-eventos-v1.
/// </summary>
/// <param name="ReceptorCui">
/// CUI del receptor para la factura (HU-005). Opcional: si no se envia, o llega
/// vacio, el servicio lo resuelve a "CF". Cuando se envia, debe ser "CF" o una
/// cadena de solo digitos; cualquier otra cosa es 400.
/// </param>
/// <param name="DireccionEntrega">
/// Direccion de entrega a domicilio. Opcional para no romper a quien crea ordenes
/// sin ella; se ignora si el metodo es recogida en tienda. Es lo que Despacho le
/// manda a LogiExpress al pedir la guia.
/// </param>
public sealed record CrearOrdenRequest(
    string ClienteId,
    Canal Canal,
    MetodoEntrega MetodoEntrega,
    Moneda Moneda,
    string MedioPago,
    decimal Impuestos,
    IReadOnlyList<CrearLineaOrdenRequest> Lineas,
    string? UbicacionSolicitadaId = null,
    string? ReceptorCui = null,
    DireccionEntregaData? DireccionEntrega = null);

public sealed record CrearLineaOrdenRequest(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario);
