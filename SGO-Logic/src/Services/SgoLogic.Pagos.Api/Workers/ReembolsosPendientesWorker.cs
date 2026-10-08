using Microsoft.Extensions.Options;
using SgoLogic.Pagos.Api.Application.Pagos;

namespace SgoLogic.Pagos.Api.Workers;

/// <summary>
/// Cada Pagos:Reembolsos:IntervaloSegundos ejecuta
/// <see cref="ServicioReembolsosPendientes.ProcesarPendientesAsync" /> en un alcance nuevo
/// mientras haya reembolsos vencidos por procesar. Un fallo de una pasada (base caida)
/// se registra y se reintenta en la siguiente; nunca tumba el proceso.
/// </summary>
public sealed class ReembolsosPendientesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReembolsosOptions _opciones;
    private readonly ILogger<ReembolsosPendientesWorker> _logger;

    public ReembolsosPendientesWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ReembolsosOptions> opciones,
        ILogger<ReembolsosPendientesWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _opciones.IntervaloSegundos)));

        do
        {
            try
            {
                int procesados;
                do
                {
                    using var scope = _scopeFactory.CreateScope();
                    procesados = await scope.ServiceProvider.GetRequiredService<ServicioReembolsosPendientes>()
                        .ProcesarPendientesAsync(stoppingToken);
                }
                while (procesados >= Math.Max(1, _opciones.Lote) && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo la pasada de reembolsos pendientes; se reintenta en la siguiente.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }
}
