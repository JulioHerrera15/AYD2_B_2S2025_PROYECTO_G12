using RabbitMQ.Client;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Declara la topologia transversal (exchanges y dead letter). Idempotente: RabbitMQ
/// no falla si se vuelve a declarar algo identico a lo ya existente. Se llama una
/// vez al arranque y, de forma defensiva, al inicio de cada ciclo del
/// OutboxPublisher y de cada suscripcion, por si el proceso arranco con el broker
/// caido y la declaracion inicial no llego a correr.
/// </summary>
public sealed class DeclaradorTopologia
{
    private readonly IConexionRabbitMq _conexion;

    public DeclaradorTopologia(IConexionRabbitMq conexion)
    {
        _conexion = conexion;
    }

    public void Declarar()
    {
        using var canal = _conexion.ObtenerConexion().CreateModel();
        DeclararEn(canal);
    }

    public static void DeclararEn(IModel canal)
    {
        canal.ExchangeDeclare(TopologiaMensajeria.ExchangeEventos, ExchangeType.Topic, durable: true, autoDelete: false);
        canal.ExchangeDeclare(TopologiaMensajeria.ExchangeSat, ExchangeType.Direct, durable: true, autoDelete: false);
        canal.ExchangeDeclare(TopologiaMensajeria.ExchangeErp, ExchangeType.Direct, durable: true, autoDelete: false);
        canal.ExchangeDeclare(TopologiaMensajeria.ExchangeDeadLetter, ExchangeType.Fanout, durable: true, autoDelete: false);

        canal.QueueDeclare(
            TopologiaMensajeria.ColaDeadLetter,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object> { ["x-queue-type"] = "quorum" });

        canal.QueueBind(TopologiaMensajeria.ColaDeadLetter, TopologiaMensajeria.ExchangeDeadLetter, routingKey: string.Empty);
    }
}