using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.IntegracionErp.Worker.Application;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Worker.Workers;

public sealed class ErpConciliacionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory; private readonly ErpOptions _opciones; private readonly IErpBatchLock _batchLock; private readonly ILogger<ErpConciliacionWorker> _logger;
    public ErpConciliacionWorker(IServiceScopeFactory scopeFactory, IOptions<ErpOptions> opciones, IErpBatchLock batchLock, ILogger<ErpConciliacionWorker> logger) { _scopeFactory = scopeFactory; _opciones = opciones.Value; _batchLock = batchLock; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EjecutarAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, _opciones.IntervaloConciliacionHoras)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarAsync(stoppingToken);
        }
    }

    private async Task EjecutarAsync(CancellationToken cancelacion)
    {
        try
        {
            await using var lease = await _batchLock.TryAcquireAsync(cancelacion);
            if (lease is null)
            {
                _logger.LogDebug("Otra replica ya esta ejecutando una operacion ERP; se omite este ciclo de conciliacion.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ConsolidacionErpService>().ConciliarAsync(cancelacion);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Error en conciliacion ERP"); }
    }
}
