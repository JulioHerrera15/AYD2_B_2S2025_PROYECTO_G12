using RabbitMQ.Client;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Publica con delivery_mode 2 (persistente), content_type application/json,
/// content_encoding utf-8 y confirms del publicador. El metodo estatico
/// <see cref="PublicarEnCanalConfirmado"/> se reusa desde el OutboxPublisher, que
/// abre un solo canal para todo un lote en vez de uno por mensaje.
/// </summary>
public sealed class PublicadorRabbitMq : IPublicadorMensajes
{
    private static readonly TimeSpan TiempoEsperaConfirmacion = TimeSpan.FromSeconds(5);

    private readonly IConexionRabbitMq _conexion;

    public PublicadorRabbitMq(IConexionRabbitMq conexion)
    {
        _conexion = conexion;
    }

    public Task PublicarAsync(
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> cuerpo,
        CancellationToken cancelacion = default)
    {
        using var canal = _conexion.ObtenerConexion().CreateModel();
        canal.ConfirmSelect();

        PublicarEnCanalConfirmado(canal, exchange, routingKey, cuerpo);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publica en un canal ya abierto con ConfirmSelect() ya invocado, y espera la
    /// confirmacion de ese mensaje puntual. Lanza PublicacionNoConfirmadaException
    /// si el broker no confirma o rechaza.
    /// </summary>
    public static void PublicarEnCanalConfirmado(
        IModel canal,
        string exchange,
        string routingKey,
        ReadOnlyMemory<byte> cuerpo)
    {
        var propiedades = canal.CreateBasicProperties();
        propiedades.Persistent = true;
        propiedades.ContentType = "application/json";
        propiedades.ContentEncoding = "utf-8";

        canal.BasicPublish(exchange, routingKey, mandatory: false, propiedades, cuerpo);

        if (!canal.WaitForConfirms(TiempoEsperaConfirmacion))
        {
            throw new PublicacionNoConfirmadaException(exchange, routingKey);
        }
    }
}