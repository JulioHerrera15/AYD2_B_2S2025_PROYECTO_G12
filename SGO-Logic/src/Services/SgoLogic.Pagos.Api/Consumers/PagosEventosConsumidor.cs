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
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Consumers;

/// <summary>
/// Consume la cola <c>pagos.ordenes</c> (bindings a
/// <c>ordenes.cobro.solicitado</c>, <c>ordenes.devolucion.registrada</c> y
/// <c>ordenes.orden.cancelada</c>) y delega a <see cref="ServicioCobro" />,
/// <see cref="ServicioReembolso" /> o <see cref="ServicioReembolsosPendientes" />.
///
/// No usa EventConsumerBase porque esta cola recibe multiples tipos de data
/// sobre un solo binding-set y la clase base esta tipada a un unico TData. Aun
/// asi replica su contrato: declara una cola quorum con dead letter, deduplica
/// por eventId antes de aplicar, y puebla EventContext para que el evento
/// derivado herede transactionId y causationId.
///
/// A diferencia de EventConsumerBase, este consumidor llama a SaveChangesAsync
/// explicitamente despues de ManejarAsync, para que el intento, la fila de
/// outbox y la marca de procesado queden en una sola transaccion.
///
/// Politica de errores (TOPOLOGIA-MENSAJERIA seccion 4):
/// <list type="bullet">
///   <item>Evento desconocido: warning y ACK, sin requeue.</item>
///   <item>Fallo transitorio (base caida, broker): NACK con requeue.</item>
///   <item>Un mensaje malo nunca tumba el proceso.</item>
/// </list>
/// </summary>
public sealed class PagosEventosConsumidor : BackgroundService
{
    /// <summary>Cola propia del contexto Pagos para los eventos de Ordenes.</summary>
    public const string NombreCola = "pagos.ordenes";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "ordenes.cobro.solicitado"),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DevolucionRegistrada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenCancelada.RoutingKey)
    };

    private const string NombreServicio = "pagos";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<PagosEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public PagosEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<PagosEventosConsumidor> logger)
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
            _logger.LogError("Mensaje vacio en la cola {Cola}", NombreCola);
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<PagosDbContext>();

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
            switch (sobre.EventType)
            {
                case "CobroSolicitado":
                    await ManejarCobroAsync(sobre, contexto, scope.ServiceProvider, cancelacion);
                    break;

                case DevolucionRegistrada.EventType:
                    await ManejarDevolucionAsync(sobre, contexto, scope.ServiceProvider, cancelacion);
                    break;

                case OrdenCancelada.EventType:
                    await ManejarCancelacionAsync(sobre, scope.ServiceProvider, cancelacion);
                    break;

                default:
                    _logger.LogWarning(
                        "Tipo de evento desconocido {EventType} en {Cola}; se hace ack sin procesar.",
                        sobre.EventType, NombreCola);
                    return true;
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
                sobre.EventType,
                sobre.EventId,
                NombreCola);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }

    private static async Task ManejarCobroAsync(
        IntegrationEvent<JsonElement> sobre,
        PagosDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var datos = sobre.Data.Deserialize<CobroSolicitadoData>(SgoJsonSerializerOptions.Default)
            ?? throw new JsonException("El campo data no coincide con CobroSolicitadoData.");

        var servicio = servicios.GetRequiredService<ServicioCobro>();

        var solicitud = new SolicitudCobro(
            datos.OrdenId,
            datos.ClienteId,
            datos.Canal,
            datos.Moneda,
            datos.Monto,
            datos.MedioPago,
            datos.TokenMedioPago,
            datos.UltimosCuatro,
            datos.Marca);

        await servicio.ProcesarSolicitudAsync(solicitud, cancelacion);
    }

    private static async Task ManejarDevolucionAsync(
        IntegrationEvent<JsonElement> sobre,
        PagosDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var datos = sobre.Data.Deserialize<DevolucionRegistradaData>(SgoJsonSerializerOptions.Default)
            ?? throw new JsonException("El campo data no coincide con DevolucionRegistradaData.");

        var servicio = servicios.GetRequiredService<ServicioReembolso>();
        await servicio.ProcesarReembolsoAsync(datos, cancelacion);
    }

    /// <summary>
    /// Orden pagada con tarjeta y cancelada: registra el reembolso PENDIENTE (lo envia el
    /// worker). Se guarda en el mismo SaveChanges que la marca de procesado.
    /// </summary>
    private static async Task ManejarCancelacionAsync(
        IntegrationEvent<JsonElement> sobre,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var datos = sobre.Data.Deserialize<OrdenCanceladaData>(SgoJsonSerializerOptions.Default)
            ?? throw new JsonException("El campo data no coincide con OrdenCanceladaData.");

        await servicios.GetRequiredService<ServicioReembolsosPendientes>().CrearPorCancelacionAsync(datos, cancelacion);
    }
}
