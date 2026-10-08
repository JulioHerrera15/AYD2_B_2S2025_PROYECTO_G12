using Microsoft.Extensions.Options;
using SgoLogic.Credito.Api.Application;

namespace SgoLogic.Credito.Api.Workers;

/// <summary>
/// Cada Credito:Cobranza:IntervaloSegundos (60 por defecto) ejecuta
/// <see cref="ServicioVencimientos.ProcesarAsync" /> en un alcance nuevo. Un ciclo
/// fallido se registra y no detiene el worker.
/// </summary>
public sealed class VencimientosCreditoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CobranzaOptions _opciones;
    private readonly ILogger<VencimientosCreditoWorker> _logger;

    public VencimientosCreditoWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CobranzaOptions> opciones,
        ILogger<VencimientosCreditoWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(5, _opciones.IntervaloSegundos));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ServicioVencimientos>().ProcesarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el ciclo de cobranza (recordatorios y vencimientos); se reintenta en el siguiente.");
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
