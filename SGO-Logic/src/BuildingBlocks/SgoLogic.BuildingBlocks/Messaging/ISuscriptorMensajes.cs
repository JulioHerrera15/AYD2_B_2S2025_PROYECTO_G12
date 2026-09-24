namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Suscripcion con ack manual a una cola ya declarada por su dueno. El manejador
/// devuelve true para ack (procesado, o duplicado descartado a proposito) y false
/// para nack con requeue: el mensaje vuelve a la misma cola quorum, que cuenta esa
/// redelivery contra x-delivery-limit (declarado por DeclararCola); al agotarlo,
/// la propia cola lo manda a sgo.eventos.dlx sin que este codigo intervenga.
/// </summary>
public interface ISuscriptorMensajes
{
    /// <summary>
    /// Empieza a consumir. Devuelve un IDisposable que cierra el canal y detiene
    /// el consumo al liberarse (por ejemplo, cuando el BackgroundService se detiene).
    /// </summary>
    IDisposable Suscribir(string cola, Func<ReadOnlyMemory<byte>, Task<bool>> manejador);
}