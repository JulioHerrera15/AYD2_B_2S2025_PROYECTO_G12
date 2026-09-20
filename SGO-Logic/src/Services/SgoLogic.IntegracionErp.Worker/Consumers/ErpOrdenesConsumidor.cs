using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Worker.Consumers;

public sealed class ErpOrdenesConsumidor : BackgroundService
{
    public const string NombreCola = "erp.ordenes";
    private const string NombreServicio = "integracion-erp";
    private static readonly IReadOnlyCollection<BindingCola> Bindings =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenConfirmada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenCancelada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenCertificadaFiscalmente.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DevolucionRegistrada.RoutingKey)
    ];

    private readonly IServiceScopeFactory _scopeFactory; private readonly IConexionRabbitMq _conexion; private readonly ISuscriptorMensajes _suscriptor; private readonly ILogger<ErpOrdenesConsumidor> _logger; private IDisposable? _suscripcion;
    public ErpOrdenesConsumidor(IServiceScopeFactory scopeFactory, IConexionRabbitMq conexion, ISuscriptorMensajes suscriptor, ILogger<ErpOrdenesConsumidor> logger) { _scopeFactory = scopeFactory; _conexion = conexion; _suscriptor = suscriptor; _logger = logger; }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var canal = _conexion.ObtenerConexion().CreateModel(); DeclaradorTopologia.DeclararEn(canal); DeclaradorColas.DeclararCola(canal, NombreCola, Bindings);
        _suscripcion = _suscriptor.Suscribir(NombreCola, cuerpo => ProcesarAsync(cuerpo, stoppingToken)); stoppingToken.Register(() => _suscripcion?.Dispose()); return Task.CompletedTask;
    }

    private async Task<bool> ProcesarAsync(ReadOnlyMemory<byte> cuerpo, CancellationToken cancelacion)
    {
        IntegrationEvent<JsonElement>? evento;
        try { evento = JsonSerializer.Deserialize<IntegrationEvent<JsonElement>>(cuerpo.Span, SgoJsonSerializerOptions.Default); } catch (JsonException ex) { _logger.LogError(ex, "Evento ERP invalido"); return false; }
        if (evento is null) return false;
        using var scope = _scopeFactory.CreateScope(); var contexto = scope.ServiceProvider.GetRequiredService<IntegracionErpDbContext>();
        if (await contexto.MensajesProcesados.AnyAsync(x => x.EventId == evento.EventId && x.NombreServicio == NombreServicio, cancelacion)) { MetricasSgo.MensajesDuplicadosTotal.Inc(); return true; }
        EventContext.TransactionId = evento.TransactionId; EventContext.CausationId = evento.EventId;
        try
        {
            switch (evento.EventType)
            {
                case OrdenConfirmada.EventType: await AplicarConfirmada(evento, contexto, cancelacion); break;
                case OrdenCancelada.EventType: await AplicarCancelada(evento, contexto, cancelacion); break;
                case OrdenCertificadaFiscalmente.EventType: await AplicarCertificada(evento, contexto, cancelacion); break;
                case DevolucionRegistrada.EventType: await AplicarDevolucion(evento, contexto, cancelacion); break;
                default: _logger.LogWarning("Evento ERP no soportado {EventType}", evento.EventType); return true;
            }
            contexto.MensajesProcesados.Add(new MensajeProcesado(evento.EventId, NombreServicio, DateTime.UtcNow)); await contexto.SaveChangesAsync(cancelacion); MetricasSgo.MensajesConsumidosTotal.Inc(); return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested) { _logger.LogError(ex, "Error consumiendo evento ERP {EventId}; se reencola", evento.EventId); return false; }
        finally { EventContext.TransactionId = null; EventContext.CausationId = null; }
    }

    private static async Task AplicarConfirmada(IntegrationEvent<JsonElement> e, IntegracionErpDbContext c, CancellationToken ct)
    {
        var d = e.Data.Deserialize<OrdenConfirmadaData>(SgoJsonSerializerOptions.Default) ?? throw new InvalidOperationException("OrdenConfirmada invalida");
        var venta = await c.Ventas.FirstOrDefaultAsync(x => x.OrdenId == d.OrdenId, ct);
        if (venta is null)
        {
            venta = new VentaErp { OrdenId = d.OrdenId, EventoId = e.EventId };
            c.Ventas.Add(venta);
        }
        venta.EventoId = e.EventId;
        venta.NumeroOrden = d.NumeroOrden;
        venta.Canal = d.Canal.ToString();
        venta.Tienda = d.TiendaDestinoId ?? venta.Tienda;
        venta.Monto = d.Total;
        venta.FechaVenta = d.ConfirmadaEn;
        if (venta.Estado != "CANCELADA") venta.Estado = "PENDIENTE";
        venta.ActualizadoEn = DateTime.UtcNow;
    }
    private static async Task AplicarCancelada(IntegrationEvent<JsonElement> e, IntegracionErpDbContext c, CancellationToken ct)
    {
        var d = e.Data.Deserialize<OrdenCanceladaData>(SgoJsonSerializerOptions.Default) ?? throw new InvalidOperationException("OrdenCancelada invalida");
        var venta = await c.Ventas.FirstOrDefaultAsync(x => x.OrdenId == d.OrdenId, ct);
        if (venta is null)
        {
            venta = new VentaErp { EventoId = e.EventId, OrdenId = d.OrdenId, NumeroOrden = d.NumeroOrden, Estado = "CANCELADA" };
            c.Ventas.Add(venta);
        }
        else
        {
            venta.Estado = "CANCELADA";
        }
        venta.ActualizadoEn = DateTime.UtcNow;
    }
    private static async Task AplicarCertificada(IntegrationEvent<JsonElement> e, IntegracionErpDbContext c, CancellationToken ct)
    {
        var d = e.Data.Deserialize<OrdenCertificadaFiscalmenteData>(SgoJsonSerializerOptions.Default) ?? throw new InvalidOperationException("OrdenCertificadaFiscalmente invalida");
        var venta = await c.Ventas.FirstOrDefaultAsync(x => x.OrdenId == d.OrdenId, ct);
        if (venta is null)
        {
            venta = new VentaErp { EventoId = e.EventId, OrdenId = d.OrdenId, NumeroOrden = d.NumeroOrden, Tienda = d.TiendaDestinoId ?? "", Monto = d.Total, Estado = "PENDIENTE_FISCAL" };
            c.Ventas.Add(venta);
        }
        venta.DocumentoFiscalId = d.DocumentoFiscalId;
        venta.SerieFiscal = d.Serie;
        venta.NumeroFiscal = d.NumeroDocumento;
        venta.ActualizadoEn = DateTime.UtcNow;
    }
    private static Task AplicarDevolucion(IntegrationEvent<JsonElement> e, IntegracionErpDbContext c, CancellationToken ct)
    {
        var d = e.Data.Deserialize<DevolucionRegistradaData>(SgoJsonSerializerOptions.Default) ?? throw new InvalidOperationException("DevolucionRegistrada invalida");
        if (decimal.TryParse(d.MontoReembolso, NumberStyles.Any, CultureInfo.InvariantCulture, out var monto))
        {
            c.Ventas.Add(new VentaErp { EventoId = e.EventId, DevolucionId = d.DevolucionId, TipoDevolucion = d.Tipo.ToString(), OrdenId = d.OrdenOriginalId, NumeroOrden = d.OrdenOriginalId, Canal = "DEVOLUCION", Tienda = d.UbicacionReingresoId, Monto = -monto, FechaVenta = d.RegistradaEn, Estado = "PENDIENTE" });
        }
        return Task.CompletedTask;
    }
}
