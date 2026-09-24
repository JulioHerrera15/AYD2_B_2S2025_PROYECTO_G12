using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

namespace SgoLogic.Facturacion.Api.Consumers;

/// <summary>
/// Consume la cola <c>facturacion.ordenes</c> (bindings a
/// <c>ordenes.fiscal.certificacion-solicitada</c> y
/// <c>ordenes.devolucion.registrada</c>) y genera el documento fiscal
/// correspondiente (FACTURA o NOTA_CREDITO).
///
/// No usa <c>EventConsumerBase</c> porque esta cola recibe multiples <c>data</c>
/// distintos sobre un solo binding-set y la clase base esta tipada a un unico
/// <c>TData</c>. Replica su contrato: declara una cola quorum con dead letter,
/// deduplica por <c>eventId</c> antes de aplicar, y puebla <see cref="EventContext" />
/// para que el evento derivado herede <c>transactionId</c> y <c>causationId</c>.
/// </summary>
public sealed class FacturacionEventosConsumidor : BackgroundService
{
    public const string NombreCola = "facturacion.ordenes";
    private const string NombreServicio = "facturacion";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CertificacionFiscalSolicitada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DevolucionRegistrada.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<FacturacionEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public FacturacionEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<FacturacionEventosConsumidor> logger)
    {
        _scopeFactory = scopeFactory;
        _conexion = conexion;
        _suscriptor = suscriptor;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var canal = _conexion.ObtenerConexion().CreateModel())
        {
            DeclaradorTopologia.DeclararEn(canal);
            DeclaradorColas.DeclararCola(canal, NombreCola, BindingsCola);
        }

        _suscripcion = _suscriptor.Suscribir(NombreCola, cuerpo => ProcesarAsync(cuerpo, stoppingToken));
        stoppingToken.Register(() => _suscripcion?.Dispose());

        return Task.CompletedTask;
    }

    private async Task<bool> ProcesarAsync(ReadOnlyMemory<byte> cuerpo, CancellationToken cancelacion)
    {
        IntegrationEvent<JsonElement>? sobre;
        try
        {
            sobre = JsonSerializer.Deserialize<IntegrationEvent<JsonElement>>(cuerpo.Span, SgoJsonSerializerOptions.Default);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mensaje no deserializable en la cola {Cola}; se nackea con requeue.", NombreCola);
            return false;
        }

        if (sobre is null)
        {
            _logger.LogError("Mensaje vacio en la cola {Cola}; se hace ack.", NombreCola);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();

        var yaProcesado = await contexto.Set<MensajeProcesado>()
            .AnyAsync(m => m.EventId == sobre.EventId && m.NombreServicio == NombreServicio, cancelacion);

        if (yaProcesado)
        {
            MetricasSgo.MensajesDuplicadosTotal.Inc();
            _logger.LogInformation("Mensaje duplicado descartado: {EventId} en {Cola}", sobre.EventId, NombreCola);
            return true;
        }

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        try
        {
            var servicio = scope.ServiceProvider.GetRequiredService<ServicioFacturacion>();
            var ahora = DateTime.UtcNow;

            switch (sobre.EventType)
            {
                case CertificacionFiscalSolicitada.EventType:
                    await ProcesarCertificacionFiscal(sobre, servicio, ahora, cancelacion);
                    break;

                case DevolucionRegistrada.EventType:
                    await ProcesarDevolucion(sobre, servicio, ahora, cancelacion);
                    break;

                default:
                    _logger.LogWarning(
                        "Tipo de evento desconocido {EventType} en {Cola}; se hace ack.",
                        sobre.EventType, NombreCola);
                    break;
            }

            contexto.Set<MensajeProcesado>().Add(new MensajeProcesado(sobre.EventId, NombreServicio, DateTime.UtcNow));
            await contexto.SaveChangesAsync(cancelacion);

            MetricasSgo.MensajesConsumidosTotal.Inc();
            return true;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fallo transitorio procesando {EventType} {EventId} en {Cola}; se nackea con requeue.",
                sobre.EventType, sobre.EventId, NombreCola);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }

    private static async Task ProcesarCertificacionFiscal(
        IntegrationEvent<JsonElement> sobre,
        ServicioFacturacion servicio,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var datos = sobre.Data.Deserialize<CertificacionFiscalSolicitadaData>(SgoJsonSerializerOptions.Default)
            ?? throw new InvalidOperationException("El data del evento CertificacionFiscalSolicitada es invalido.");

        await servicio.CrearDocumentoAsync(datos, sobre.TransactionId, ahora, cancelacion);
    }

    private static async Task ProcesarDevolucion(
        IntegrationEvent<JsonElement> sobre,
        ServicioFacturacion servicio,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var datos = sobre.Data.Deserialize<DevolucionRegistradaData>(SgoJsonSerializerOptions.Default)
            ?? throw new InvalidOperationException("El data del evento DevolucionRegistrada es invalido.");

        await servicio.CrearNotaCreditoAsync(datos, sobre.TransactionId, ahora, cancelacion);
    }
}
