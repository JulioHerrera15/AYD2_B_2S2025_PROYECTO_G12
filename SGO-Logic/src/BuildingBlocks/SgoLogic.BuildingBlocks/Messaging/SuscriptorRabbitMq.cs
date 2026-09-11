using Microsoft.Extensions.Logging;
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
    private readonly ILogger<SuscriptorRabbitMq> _logger;

    public SuscriptorRabbitMq(IConexionRabbitMq conexion, ILogger<SuscriptorRabbitMq> logger)
    {
        _conexion = conexion;
        _logger = logger;
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
            catch (Exception ex)
            {
                // manejador() es en la practica ProcesarAsync de EventConsumerBase,
                // que ya loguea sus propios fallos: si algo revienta hasta aca (por
                // ejemplo, un tipo no registrado en el DbContext) es un fallo fuera
                // de ese try/catch. Sin este log quedaba invisible: el mensaje
                // nackeaba en bucle silencioso hasta el dead-letter, sin una sola
                // linea en los logs que explicara por que.
                _logger.LogError(ex, "Fallo no controlado procesando un mensaje de la cola {Cola}; se nackea con requeue.", cola);
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