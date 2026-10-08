using Microsoft.Extensions.Options;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Workers;

/// <summary>
/// Cada Credito:IntervaloTimeoutSegundos (5 por defecto) ejecuta
/// <see cref="ServicioTimeoutCredito.ProcesarVencidasAsync" /> en un alcance nuevo.
/// Un ciclo fallido se registra y no detiene el worker.
/// </summary>
public sealed class TimeoutEvaluacionCreditoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CreditoSagaOptions _opciones;
    private readonly ILogger<TimeoutEvaluacionCreditoWorker> _logger;

    public TimeoutEvaluacionCreditoWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CreditoSagaOptions> opciones,
        ILogger<TimeoutEvaluacionCreditoWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(1, _opciones.IntervaloTimeoutSegundos));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ServicioTimeoutCredito>()
                    .ProcesarVencidasAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el ciclo de timeout de evaluaciones de credito; se reintenta en el siguiente.");
            }

            try
            {
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
