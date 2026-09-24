using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

namespace SgoLogic.Facturacion.Api.Consumers;

/// <summary>
/// Worker de reintentos de la certificacion fiscal. Cada <c>Sat:PollingIntervalMs</c>
/// barre los documentos vencidos (estado PENDIENTE con <c>proximo_intento_en</c>
/// menor o igual a ahora) y encola una solicitud de envio hacia la SAT por Outbox
/// sobre <c>sgo.sat</c>. El backoff vive en la columna, no en memoria: este worker
/// solo lee lo que ya esta vencido. Tambien cubre el primer envio: un documento
/// recien creado nace vencido.
/// </summary>
public sealed class WorkerReintentosSat : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SatOptions _opciones;
    private readonly ILogger<WorkerReintentosSat> _logger;

    public WorkerReintentosSat(
        IServiceScopeFactory scopeFactory,
        IOptions<SatOptions> opciones,
        ILogger<WorkerReintentosSat> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ciclo del worker de reintentos de la SAT termino con un error inesperado.");
            }

            try
            {
                await Task.Delay(_opciones.Polling, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EjecutarCicloAsync(CancellationToken cancelacion)
    {
        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var servicio = scope.ServiceProvider.GetRequiredService<ServicioFacturacion>();

        var ahora = DateTime.UtcNow;
        var vencidos = await servicio.TomarDocumentosVencidosAsync(_opciones.LotePolling, ahora, cancelacion);

        if (vencidos.Count == 0)
        {
            return;
        }

        foreach (var documento in vencidos)
        {
            // La solicitud de envio hereda el transactionId de la certificacion
            // original, para no romper la correlacion de la saga.
            EventContext.TransactionId = documento.TransactionId;
            EventContext.CausationId = null;

            try
            {
                servicio.ReencolarEnvio(documento, ahora);
            }
            finally
            {
                EventContext.TransactionId = null;
                EventContext.CausationId = null;
            }
        }

        await contexto.SaveChangesAsync(cancelacion);

        _logger.LogInformation("Worker de reintentos de la SAT encolo {Cantidad} documento(s) vencido(s).", vencidos.Count);
    }
}
