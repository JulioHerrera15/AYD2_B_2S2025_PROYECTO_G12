using RabbitMQ.Client;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Punto unico de acceso a la conexion compartida con RabbitMQ dentro del proceso.
/// </summary>
public interface IConexionRabbitMq
{
    /// <summary>
    /// Devuelve la conexion abierta, creandola (o recreandola si se cerro) de ser
    /// necesario. Lanza si el broker no esta disponible: quien llama decide como
    /// reaccionar (el OutboxPublisher, por ejemplo, aborta el ciclo de polling en
    /// curso sin tocar filas).
    /// </summary>
    IConnection ObtenerConexion();
}