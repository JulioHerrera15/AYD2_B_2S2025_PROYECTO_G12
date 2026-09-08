namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>Publicacion de un solo mensaje, con confirms del publicador, fuera del flujo de Outbox.</summary>
public interface IPublicadorMensajes
{
    Task PublicarAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> cuerpo,
        CancellationToken cancelacion = default);
}