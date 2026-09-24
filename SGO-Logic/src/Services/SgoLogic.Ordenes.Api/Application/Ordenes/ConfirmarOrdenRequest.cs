namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Cuerpo opcional de la confirmacion de una orden. El motivo queda registrado en
/// la transicion y en el evento del outbox.
/// </summary>
public sealed record ConfirmarOrdenRequest(string? Motivo = null);
