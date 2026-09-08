namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Resultado de confirmar una orden. <see cref="YaConfirmada" /> es true cuando la
/// llamada fue idempotente: la orden ya estaba en CONFIRMADA y no se escribio nada.
/// </summary>
public sealed record ConfirmacionOrdenDto(OrdenDto Orden, bool YaConfirmada);
