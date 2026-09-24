namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Resultado de aplicar una transicion generica. <see cref="SinCambios" /> es true
/// cuando la llamada fue idempotente: la orden ya estaba en el estado destino y no
/// se escribio nada.
/// </summary>
public sealed record ResultadoTransicionOrdenDto(OrdenDto Orden, bool SinCambios);
