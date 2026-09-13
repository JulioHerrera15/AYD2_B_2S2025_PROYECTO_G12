using Microsoft.Extensions.Options;
using SgoLogic.Inventario.Api.Application.Inventario;

namespace SgoLogic.Inventario.Api.Workers;

/// <summary>
/// Disparador de HU-002. Cada <c>Inventario:ExpiracionPollingMs</c> abre un alcance
/// propio y le pide a <see cref="ServicioExpiracionReservas" /> que procese un lote
/// de reservas vencidas, igual que <c>OutboxPublisher</c> barre su tabla.
///
/// El barrido contra PostgreSQL es el mecanismo autoritativo y no Redis: las
/// notificaciones de expiracion de claves son fire-and-forget, asi que una caida del
/// servicio en el instante justo perderia la liberacion para siempre. Con el barrido,
/// una reserva vencida sigue vencida y se toma en el siguiente ciclo.
///
/// Aca no hay logica de negocio: solo el temporizador y el alcance.
/// </summary>
public sealed class ExpiracionReservasWorker : BackgroundService
{
    private readonly IServiceScopeFactory _fabricaAlcances;
    private readonly InventarioOptions _opciones;
    private readonly ILogger<ExpiracionReservasWorker> _logger;

    public ExpiracionReservasWorker(
        IServiceScopeFactory fabricaAlcances,
        IOptions<InventarioOptions> opciones,
        ILogger<ExpiracionReservasWorker> logger)
    {
        _fabricaAlcances = fabricaAlcances;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = _opciones.ExpiracionPollingMs > 0 ? _opciones.ExpiracionPollingMs : 5000;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var alcance = _fabricaAlcances.CreateScope();
                var servicio = alcance.ServiceProvider.GetRequiredService<ServicioExpiracionReservas>();

                var expiradas = await servicio.ProcesarLoteAsync(stoppingToken);

                if (expiradas > 0)
                {
                    _logger.LogInformation("Barrido de expiracion: {Expiradas} reserva(s) liberadas por TTL.", expiradas);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un ciclo que revienta entero (la base no responde, por ejemplo) no
                // debe tumbar el worker: se reintenta en el siguiente intervalo.
                _logger.LogError(ex, "Ciclo de ExpiracionReservasWorker termino con un error inesperado.");
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
