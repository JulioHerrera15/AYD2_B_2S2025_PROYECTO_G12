using System.Reflection;
using RabbitMQ.Client;
using SgoLogic.BuildingBlocks.Messaging;

namespace SgoLogic.Credito.Tests.Dobles;

/// <summary>
/// Conexion a RabbitMQ que no hace nada: CreateModel devuelve un canal cuyos
/// metodos (ExchangeDeclare, QueueDeclare, QueueBind, Dispose) no tienen efecto.
/// Basta para que EventConsumerBase declare su cola al arrancar sin broker.
/// </summary>
public sealed class ConexionRabbitMqInerte : IConexionRabbitMq
{
    public IConnection ObtenerConexion() => DispatchProxy.Create<IConnection, ProxyInerte>();
}

/// <summary>Responde cualquier llamada con el valor por defecto; un IModel pedido es otro proxy inerte.</summary>
public class ProxyInerte : DispatchProxy
{
    protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos)
    {
        var tipo = metodo?.ReturnType;
        if (tipo is null || tipo == typeof(void))
        {
            return null;
        }

        if (tipo == typeof(IModel))
        {
            return DispatchProxy.Create<IModel, ProxyInerte>();
        }

        return tipo.IsValueType ? Activator.CreateInstance(tipo) : null;
    }
}

/// <summary>
/// Suscriptor que guarda el manejador en lugar de consumir de una cola, para que la
/// prueba entregue los mensajes a mano.
/// </summary>
public sealed class SuscriptorMensajesFalso : ISuscriptorMensajes
{
    public string? Cola { get; private set; }

    public Func<ReadOnlyMemory<byte>, Task<bool>>? Manejador { get; private set; }

    public IDisposable Suscribir(string cola, Func<ReadOnlyMemory<byte>, Task<bool>> manejador)
    {
        Cola = cola;
        Manejador = manejador;
        return new Suscripcion();
    }

    public Task<bool> EntregarAsync(byte[] cuerpo) =>
        Manejador is null
            ? throw new InvalidOperationException("El consumidor no se suscribio.")
            : Manejador(cuerpo);

    private sealed class Suscripcion : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
