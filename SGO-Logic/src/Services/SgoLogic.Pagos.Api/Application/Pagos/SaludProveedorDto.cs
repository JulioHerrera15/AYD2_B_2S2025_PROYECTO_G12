namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Estado del disyuntor hacia PayKlik para la demo
/// (<c>GET /api/pagos/salud-proveedor</c>, punto 6 de HU-004).
/// </summary>
/// <param name="Estado">Estado del circuito: <c>CERRADO</c>, <c>ABIERTO</c> o <c>SEMIABIERTO</c>.</param>
/// <param name="FallosRecientes">Fallos de proveedor registrados dentro de la ventana de observacion.</param>
/// <param name="UltimaApertura">Hora UTC de la ultima vez que el circuito se abrio, o <c>null</c> si nunca se abrio.</param>
public sealed record SaludProveedorDto(
    string Estado,
    int FallosRecientes,
    DateTime? UltimaApertura);
