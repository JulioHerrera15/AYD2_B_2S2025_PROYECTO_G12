using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Suscriptor sobre un canal dedicado por suscripcion, con prefetch acotado y ack
/// manual. Redeclara la topologia transversal de forma defensiva antes de
/// consumir, por si el proceso arranco con el broker caido.
/// </summary>
public sealed class SuscriptorRabbitMq : ISuscriptorMensajes
{
    private const ushort PrefetchCount = 10;

    private readonly IConexionRabbitMq _conexion;

    public SuscriptorRabbitMq(IConexionRabbitMq conexion)
    {
        _conexion = conexion;
    }

    public IDisposable Suscribir(string cola, Func<ReadOnlyMemory<byte>, Task<bool>> manejador)
    {
        var canal = _conexion.ObtenerConexion().CreateModel();
        DeclaradorTopologia.DeclararEn(canal);
        canal.BasicQos(prefetchSize: 0, prefetchCount: PrefetchCount, global: false);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.Received += async (_, evento) =>
        {
            bool exito;
            try
            {
                exito = await manejador(evento.Body);
            }
            catch
            {
                exito = false;
            }

            if (exito)
            {
                canal.BasicAck(evento.DeliveryTag, multiple: false);
            }
            else
            {
                // requeue: true a proposito. Es lo que hace que la cola quorum
                // cuente la redelivery: x-delivery-limit (declarado por
                // DeclararCola) es quien decide, tras agotarlo, mandar el mensaje
                // solo a sgo.eventos.dlx. Con requeue: false se perderia ese
                // conteo y el mensaje caeria a dead letter en el primer fallo.
                canal.BasicNack(evento.DeliveryTag, multiple: false, requeue: true);
            }
        };

        canal.BasicConsume(cola, autoAck: false, consumer: consumidor);

        return canal;
    }
}