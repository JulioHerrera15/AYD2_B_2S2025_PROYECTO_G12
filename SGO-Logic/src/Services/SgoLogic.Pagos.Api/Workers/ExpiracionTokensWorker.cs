using Microsoft.Extensions.Options;
using SgoLogic.Pagos.Api.Application.Tokens;

namespace SgoLogic.Pagos.Api.Workers;

/// <summary>
/// Barre la boveda transitoria cada <see cref="BovedaOptions.IntervaloExpiracionSegundos" />:
/// marca EXPIRADO los tokens activos vencidos y borra sus datos cifrados
/// (<see cref="ServicioTokensPago.ExpirarVencidosAsync" />). Un fallo de una pasada
/// (base caida) se registra y se reintenta en la siguiente; nunca tumba el proceso.
/// </summary>
public sealed class ExpiracionTokensWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BovedaOptions _opciones;
    private readonly ILogger<ExpiracionTokensWorker> _logger;

    public ExpiracionTokensWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BovedaOptions> opciones,
        ILogger<ExpiracionTokensWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _opciones.IntervaloExpiracionSegundos)));

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var servicio = scope.ServiceProvider.GetRequiredService<ServicioTokensPago>();

                int expirados;
                do
                {
                    expirados = await servicio.ExpirarVencidosAsync(stoppingToken);
                    if (expirados > 0)
                    {
                        _logger.LogInformation("{Cantidad} tokens de pago vencidos marcados EXPIRADO.", expirados);
                    }
                }
                while (expirados > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo la expiracion de tokens de pago; se reintenta en la siguiente pasada.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }
}
