using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Conexion resiliente y compartida a RabbitMQ. Se registra como singleton: todo
/// el proceso reusa la misma conexion TCP y abre un canal (IModel) por unidad de
/// trabajo (una publicacion, una suscripcion), que es el uso recomendado por el
/// cliente. AutomaticRecoveryEnabled hace que, si la conexion se cae, el cliente
/// reconecte solo y vuelva a declarar la topologia que el registro en esta misma
/// conexion.
/// </summary>
public sealed class ConexionRabbitMq : IConexionRabbitMq, IDisposable
{
    private readonly ConnectionFactory _fabrica;
    private readonly object _bloqueo = new();
    private IConnection? _conexion;

    public ConexionRabbitMq(IConfiguration configuracion)
    {
        var cadenaConexion = configuracion.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Falta la cadena de conexion 'RabbitMq' en la configuracion.");

        _fabrica = new ConnectionFactory
        {
            Uri = new Uri(cadenaConexion),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            DispatchConsumersAsync = true
        };
    }

    public IConnection ObtenerConexion()
    {
        if (_conexion is { IsOpen: true })
        {
            return _conexion;
        }

        lock (_bloqueo)
        {
            if (_conexion is { IsOpen: true })
            {
                return _conexion;
            }

            _conexion?.Dispose();
            _conexion = _fabrica.CreateConnection();
            return _conexion;
        }
    }

    public void Dispose()
    {
        _conexion?.Dispose();
    }
}