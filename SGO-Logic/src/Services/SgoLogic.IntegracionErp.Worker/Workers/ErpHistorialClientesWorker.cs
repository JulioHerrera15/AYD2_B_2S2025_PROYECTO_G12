using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Worker.Workers;

public sealed class ErpHistorialClientesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IErpHistorialLock _lock;
    private readonly ErpOptions _opciones;
    private readonly ILogger<ErpHistorialClientesWorker> _logger;

    public ErpHistorialClientesWorker(
        IServiceScopeFactory scopeFactory,
        IErpHistorialLock erpHistorialLock,
        IOptions<ErpOptions> opciones,
        ILogger<ErpHistorialClientesWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _lock = erpHistorialLock;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_opciones.EjecutarHistorialAlArrancar)
            await EjecutarAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _opciones.IntervaloHistorialMinutos)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await EjecutarAsync(stoppingToken);
    }

    private async Task EjecutarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var lease = await _lock.TryAcquireAsync(cancellationToken);
            if (lease is null)
            {
                _logger.LogDebug("Otra replica ya esta ejecutando el historial crediticio ERP; se omite este ciclo.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var cliente = scope.ServiceProvider.GetRequiredService<IClienteErp>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
            var clientes = await cliente.ObtenerHistorialClientesAsync(cancelacion: cancellationToken);

            var contador = 0;
            foreach (var historial in clientes)
            {
                var data = new HistorialClienteErpActualizadoData(
                    ClienteId: historial.Email,
                    CodigoClienteErp: historial.CodigoCliente,
                    ClienteDesde: historial.ClienteDesde,
                    ComprasUltimos12Meses: historial.ComprasUltimos12Meses,
                    MontoCompradoUltimos12Meses: historial.MontoCompradoUltimos12Meses,
                    Moneda: historial.Moneda,
                    PagosPuntuales: historial.PagosPuntuales,
                    PagosTardios: historial.PagosTardios,
                    MaxDiasAtraso: historial.MaxDiasAtraso,
                    Devoluciones: historial.Devoluciones,
                    IncidentesGraves: historial.IncidentesGraves,
                    OtrosIncidentes: historial.OtrosIncidentes,
                    FechaCorte: historial.FechaCorte,
                    LoteOrigen: historial.LoteOrigen ?? $"mock-erp:{historial.ActualizadoEn:O}");

                outbox.Encolar(
                    HistorialClienteErpActualizado.EventType,
                    HistorialClienteErpActualizado.AggregateType,
                    historial.Email,
                    1,
                    TopologiaMensajeria.ExchangeEventos,
                    HistorialClienteErpActualizado.RoutingKey,
                    data);

                contador++;
            }

            _logger.LogInformation("Se publicaron {Cantidad} historiales crediticios del ERP", contador);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sincronizando historial crediticio del ERP");
        }
    }
}
