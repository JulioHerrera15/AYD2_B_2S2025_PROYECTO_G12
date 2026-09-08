using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Declara la topologia transversal al arrancar el proceso, antes que corran el
/// OutboxPublisher o los consumidores (el host de .NET arranca los IHostedService
/// en el orden en que se registraron). Si el broker esta caido en ese instante,
/// solo lo registra como advertencia y deja el proceso arrancar: el
/// OutboxPublisher y cada suscripcion vuelven a intentar la declaracion por su
/// cuenta antes de publicar o consumir.
/// </summary>
public sealed class TopologiaInicialHostedService : IHostedService
{
    private readonly DeclaradorTopologia _declarador;
    private readonly ILogger<TopologiaInicialHostedService> _logger;

    public TopologiaInicialHostedService(DeclaradorTopologia declarador, ILogger<TopologiaInicialHostedService> logger)
    {
        _declarador = declarador;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _declarador.Declarar();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo declarar la topologia de RabbitMQ al arrancar; se reintentara mas adelante.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}