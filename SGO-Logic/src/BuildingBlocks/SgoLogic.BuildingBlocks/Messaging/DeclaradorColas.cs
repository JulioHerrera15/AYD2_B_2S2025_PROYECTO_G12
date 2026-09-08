using RabbitMQ.Client;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>Un binding a declarar sobre la cola propia de un contexto.</summary>
public sealed record BindingCola(string Exchange, string RoutingKey);

/// <summary>
/// Helper para que cada contexto declare su propia cola de consumo, sin repetir en
/// cada servicio los argumentos de quorum + dead letter. El nombre de la cola y sus
/// bindings los define el dueno del contexto; esto solo fija la forma.
/// </summary>
public static class DeclaradorColas
{
    /// <summary>
    /// Declara una cola quorum, durable, con dead-letter hacia sgo.eventos.dlx y un
    /// limite de reintentos de entrega, y la liga a los bindings indicados.
    /// </summary>
    public static void DeclararCola(
        IModel canal,
        string nombre,
        IEnumerable<BindingCola> bindings,
        int limiteReintentos = TopologiaMensajeria.LimiteReintentosPorDefecto)
    {
        canal.QueueDeclare(
            nombre,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = TopologiaMensajeria.ExchangeDeadLetter,
                ["x-delivery-limit"] = limiteReintentos
            });

        foreach (var binding in bindings)
        {
            canal.QueueBind(nombre, binding.Exchange, binding.RoutingKey);
        }
    }
}