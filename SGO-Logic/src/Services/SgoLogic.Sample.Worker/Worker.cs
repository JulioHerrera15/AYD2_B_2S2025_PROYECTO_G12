namespace SgoLogic.Sample.Worker;

/// <summary>
/// Servicio de fondo de ejemplo: solo escribe un log periodico para demostrar
/// la plantilla de Worker. Sin logica de negocio.
/// </summary>
public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker de ejemplo en ejecucion: {Momento}", DateTime.UtcNow);
            await Task.Delay(Intervalo, stoppingToken);
        }
    }
}