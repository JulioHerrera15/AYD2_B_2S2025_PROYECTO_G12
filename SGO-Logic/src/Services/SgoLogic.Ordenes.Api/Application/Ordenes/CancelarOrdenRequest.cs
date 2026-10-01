namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Cuerpo de la cancelacion de una orden por su cliente. <see cref="ClienteId" />
/// debe coincidir con el dueno de la orden; el motivo queda en la transicion y en
/// OrdenCancelada.
/// </summary>
public sealed record CancelarOrdenRequest(string ClienteId, string? Motivo = null);
