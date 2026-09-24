namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// El broker no confirmo la publicacion dentro del tiempo de espera, o la
/// rechazo explicitamente (nack). Quien publica (el OutboxPublisher) trata esto
/// como un intento fallido del mensaje, no como caida de la conexion.
/// </summary>
public sealed class PublicacionNoConfirmadaException : Exception
{
    public PublicacionNoConfirmadaException(string exchange, string routingKey)
        : base($"El broker no confirmo la publicacion en exchange '{exchange}' con routing key '{routingKey}'.")
    {
    }
}