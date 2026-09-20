using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.IntegracionPos;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.IntegracionPos.Worker.Application.Pos;
using SgoLogic.IntegracionPos.Worker.Infrastructure.Persistencia;

namespace SgoLogic.IntegracionPos.Worker.Consumers;

public sealed class PosEventosConsumidor : BackgroundService
{
    private const string Cola = "pos.ordenes";
    private const string NombreServicio = "integracion-pos";
    private static readonly IReadOnlyCollection<BindingCola> Bindings =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenConfirmada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenCancelada.RoutingKey)
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly IClientePos _clientePos;
    private readonly IServicioMapeoPos _servicioMapeoPos;
    private readonly ILogger<PosEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public PosEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        IClientePos clientePos,
        IServicioMapeoPos servicioMapeoPos,
        ILogger<PosEventosConsumidor> logger)
    {
        _scopeFactory = scopeFactory;
        _conexion = conexion;
        _suscriptor = suscriptor;
        _clientePos = clientePos;
        _servicioMapeoPos = servicioMapeoPos;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var canal = _conexion.ObtenerConexion().CreateModel())
        {
            DeclaradorTopologia.DeclararEn(canal);
            DeclaradorColas.DeclararCola(canal, Cola, Bindings);
        }

        _suscripcion = _suscriptor.Suscribir(Cola, cuerpo => ProcesarAsync(cuerpo, stoppingToken));
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
            _logger.LogError(ex, "Mensaje no deserializable en la cola {Cola}; se nackea con requeue.", Cola);
            return false;
        }

        if (sobre is null)
        {
            _logger.LogWarning("Mensaje vacio en la cola {Cola}; se marca como procesado.", Cola);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<IntegracionPosDbContext>();

        var yaProcesado = await contexto.Set<MensajeProcesado>()
            .AnyAsync(m => m.EventId == sobre.EventId && m.NombreServicio == NombreServicio, cancelacion);

        if (yaProcesado)
        {
            MetricasSgo.MensajesDuplicadosTotal.Inc();
            _logger.LogInformation("Mensaje duplicado descartado: {EventId} en {Cola}", sobre.EventId, Cola);
            return true;
        }

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        try
        {
            string? tiendaDestinoId = null;
            string? numeroOrden = null;
            string? ordenId = null;
            decimal total = 0m;
            string tipoEvento = sobre.EventType;

            switch (sobre.EventType)
            {
                case OrdenConfirmada.EventType:
                    var ordenConfirmada = sobre.Data.Deserialize<OrdenConfirmadaData>(SgoJsonSerializerOptions.Default)
                        ?? throw new InvalidOperationException("Data de OrdenConfirmada invalida");
                    tiendaDestinoId = ordenConfirmada.TiendaDestinoId;
                    numeroOrden = ordenConfirmada.NumeroOrden;
                    ordenId = ordenConfirmada.OrdenId;
                    total = ordenConfirmada.Total;
                    break;
                case OrdenCancelada.EventType:
                    var ordenCancelada = sobre.Data.Deserialize<OrdenCanceladaData>(SgoJsonSerializerOptions.Default)
                        ?? throw new InvalidOperationException("Data de OrdenCancelada invalida");
                    tiendaDestinoId = ordenCancelada.TiendaDestinoId;
                    numeroOrden = ordenCancelada.NumeroOrden;
                    ordenId = ordenCancelada.OrdenId;
                    total = ordenCancelada.MontoReembolso ?? 0m;
                    break;
                default:
                    _logger.LogWarning("Evento no manejado {EventType}; se descarta", sobre.EventType);
                    return true;
            }

            var posInstanciaId = _servicioMapeoPos.ResolverPosInstanciaId(tiendaDestinoId);

            var respuesta = await _clientePos.EnviarAsync(
                tiendaDestinoId ?? "",
                posInstanciaId,
                sobre.EventId,
                ordenId,
                tipoEvento,
                total,
                cancelacion);

            if (!respuesta.Aceptado && !respuesta.Duplicado)
            {
                contexto.RegistrosEnvioPos.Add(new RegistroEnvioPos
                {
                    EventoId = sobre.EventId,
                    OrdenId = ordenId,
                    NumeroOrden = numeroOrden ?? ordenId,
                    TiendaId = tiendaDestinoId ?? "",
                    PosInstanciaId = posInstanciaId,
                    TipoEvento = tipoEvento,
                    EstadoEnvio = respuesta.Estado,
                    CodigoHttp = respuesta.CodigoHttp,
                    Respuesta = respuesta.RespuestaRaw,
                    IntentoNumero = 1,
                    FechaEnvio = DateTime.UtcNow
                });

                var auditoriaFallida = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
                auditoriaFallida.Encolar(
                    VentaPosPropagada.EventType,
                    VentaPosPropagada.AggregateType,
                    sobre.AggregateId,
                    sequence: sobre.Sequence + 1,
                    exchange: TopologiaMensajeria.ExchangeEventos,
                    routingKey: VentaPosPropagada.RoutingKey,
                    data: new VentaPosPropagadaData(
                        ordenId,
                        numeroOrden ?? ordenId,
                        tiendaDestinoId ?? "",
                        posInstanciaId,
                        sobre.EventId,
                        tipoEvento,
                        respuesta.Estado,
                        total,
                        DateTime.UtcNow,
                        respuesta.Mensaje));

                await contexto.SaveChangesAsync(cancelacion);
                _logger.LogWarning(
                    "El POS no acepto el evento {EventId} despues de los reintentos locales; se devuelve a RabbitMQ para reentrega.",
                    sobre.EventId);
                return false;
            }

            contexto.RegistrosEnvioPos.Add(new RegistroEnvioPos
            {
                EventoId = sobre.EventId,
                OrdenId = ordenId,
                NumeroOrden = numeroOrden ?? ordenId,
                TiendaId = tiendaDestinoId ?? "",
                PosInstanciaId = posInstanciaId,
                TipoEvento = tipoEvento,
                EstadoEnvio = respuesta.Estado,
                CodigoHttp = respuesta.CodigoHttp,
                Respuesta = respuesta.RespuestaRaw,
                IntentoNumero = 1,
                FechaEnvio = DateTime.UtcNow
            });

            var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
            outboxWriter.Encolar(
                VentaPosPropagada.EventType,
                VentaPosPropagada.AggregateType,
                sobre.AggregateId,
                sequence: sobre.Sequence + 1,
                exchange: TopologiaMensajeria.ExchangeEventos,
                routingKey: VentaPosPropagada.RoutingKey,
                data: new VentaPosPropagadaData(
                    ordenId,
                    numeroOrden ?? ordenId,
                    tiendaDestinoId ?? "",
                    posInstanciaId,
                    sobre.EventId,
                    tipoEvento,
                    respuesta.Estado,
                    total,
                    DateTime.UtcNow,
                    respuesta.Mensaje));

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
            _logger.LogError(ex, "Fallo procesando {EventType} {EventId} en {Cola}; se nackea con requeue.", sobre.EventType, sobre.EventId, Cola);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }
}
