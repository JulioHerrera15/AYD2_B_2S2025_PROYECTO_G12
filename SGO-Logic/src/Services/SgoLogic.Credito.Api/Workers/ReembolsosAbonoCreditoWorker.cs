using Microsoft.Extensions.Options;
using SgoLogic.Credito.Api.Application;

namespace SgoLogic.Credito.Api.Workers;

/// <summary>
/// Cada Credito:Cobranza:IntervaloReembolsosSegundos (60 por defecto) ejecuta
/// <see cref="ServicioReembolsosAbono.SolicitarPendientesAsync" /> en un alcance nuevo:
/// los abonos en REEMBOLSO_PENDIENTE sin solicitud piden su reembolso a Pagos. Un ciclo
/// fallido se registra y no detiene el worker.
/// </summary>
public sealed class ReembolsosAbonoCreditoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CobranzaOptions _opciones;
    private readonly ILogger<ReembolsosAbonoCreditoWorker> _logger;

    public ReembolsosAbonoCreditoWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CobranzaOptions> opciones,
        ILogger<ReembolsosAbonoCreditoWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(5, _opciones.IntervaloReembolsosSegundos));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ServicioReembolsosAbono>().SolicitarPendientesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el ciclo de reembolsos de abonos pendientes; se reintenta en el siguiente.");
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
