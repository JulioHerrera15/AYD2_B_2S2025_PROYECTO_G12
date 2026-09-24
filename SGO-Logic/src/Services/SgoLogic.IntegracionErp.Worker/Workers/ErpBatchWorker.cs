using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.IntegracionErp.Worker.Application;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Worker.Workers;

public sealed class ErpBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory; private readonly ErpOptions _opciones; private readonly IErpBatchLock _batchLock; private readonly ILogger<ErpBatchWorker> _logger;
    public ErpBatchWorker(IServiceScopeFactory scopeFactory, IOptions<ErpOptions> opciones, IErpBatchLock batchLock, ILogger<ErpBatchWorker> logger) { _scopeFactory = scopeFactory; _opciones = opciones.Value; _batchLock = batchLock; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_opciones.EjecutarAlArrancar) await EjecutarAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _opciones.IntervaloConsolidacionMinutos)));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await EjecutarAsync(stoppingToken);
    }
    private async Task EjecutarAsync(CancellationToken ct)
    {
        try
        {
            await using var lease = await _batchLock.TryAcquireAsync(ct);
            if (lease is null)
            {
                _logger.LogDebug("Otra replica ya esta ejecutando la consolidacion batch ERP; se omite este ciclo.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ConsolidacionErpService>().ConsolidarYTransferirAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Error en consolidacion batch ERP"); }
    }
}
