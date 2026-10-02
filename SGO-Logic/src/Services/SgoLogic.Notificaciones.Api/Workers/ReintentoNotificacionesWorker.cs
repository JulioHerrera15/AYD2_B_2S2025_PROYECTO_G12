using SgoLogic.Notificaciones.Api.Application;

namespace SgoLogic.Notificaciones.Api.Workers;

/// <summary>
/// Reintenta cada 5 segundos las notificaciones pendientes cuyo turno llego
/// (<see cref="ServicioNotificaciones.ReintentarPendientesAsync" />). El backoff lo
/// decide cada <see cref="Domain.NotificacionPendiente" />; este worker solo las barre.
/// Un fallo de una pasada se registra y se reintenta en la siguiente.
/// </summary>
public sealed class ReintentoNotificacionesWorker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReintentoNotificacionesWorker> _logger;

    public ReintentoNotificacionesWorker(IServiceScopeFactory scopeFactory, ILogger<ReintentoNotificacionesWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(Intervalo);

        while (await temporizador.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var servicio = scope.ServiceProvider.GetRequiredService<ServicioNotificaciones>();
                await servicio.ReintentarPendientesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo el reintento de notificaciones pendientes; se intenta de nuevo en la siguiente pasada.");
            }
        }
    }
}
