using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Datos de entrada para registrar una devolucion sobre una orden entregada (HU-014).
/// </summary>
public sealed record SolicitudDevolucion(
    string OrdenId,
    TipoDevolucion Tipo,
    string UbicacionReingresoId,
    string RegistradaPor,
    IReadOnlyList<LineaDevolucionSolicitud> Lineas);

/// <summary>
/// Linea individual de una solicitud de devolucion.
/// </summary>
public sealed record LineaDevolucionSolicitud(
    string Sku,
    int Cantidad,
    string Motivo,
    string? SkuReemplazo = null);
