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
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

namespace SgoLogic.Ordenes.Api.Consumers;

/// <summary>
/// Consume la cola <c>ordenes.inventario</c> (bindings a
/// <c>inventario.reserva.confirmada</c>, <c>inventario.reserva.rechazada</c> y
/// <c>inventario.reserva.expirada</c> sobre <c>sgo.eventos</c>) y avanza la
/// saga de la orden con <see cref="ManejadorEventosInventario" />.
///
/// No usa <c>EventConsumerBase</c> porque esta cola recibe multiples <c>data</c>
/// distintos sobre un solo binding-set y la clase base esta tipada a un unico
/// <c>TData</c>. Aun asi replica su contrato: declara una cola quorum con dead
/// letter, deduplica por <c>eventId</c> antes de aplicar, y puebla
/// <see cref="EventContext" /> para que el evento derivado herede
/// <c>transactionId</c> y <c>causationId</c>.
///
/// Politica de errores (misma que PagosEventosConsumidor):
/// <list type="bullet">
///   <item>Transicion invalida o evento desconocido: warning y ACK, sin requeue.</item>
///   <item>Fallo transitorio (base caida, broker, orden aun no visible): NACK con
///   requeue, para que <c>x-delivery-limit</c> y el dead-letter de HU-008 actuen.</item>
///   <item>Un mensaje malo nunca tumba el proceso.</item>
/// </list>
/// </summary>
public sealed class InventarioEventosConsumidor : BackgroundService
{
    /// <summary>Cola propia del contexto Ordenes para los resultados de Inventario.</summary>
    public const string NombreCola = "ordenes.inventario";

    private const string NombreServicio = "ordenes";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, InventarioReservado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReservaInventarioRechazada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReservaInventarioExpirada.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<InventarioEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public InventarioEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<InventarioEventosConsumidor> logger)
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
            _logger.LogError("Mensaje vacio en la cola {Cola}.", NombreCola);
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<OrdenesDbContext>();

        var yaProcesado = await contexto.Set<MensajeProcesado>()
            .AnyAsync(m => m.EventId == sobre.EventId && m.NombreServicio == NombreServicio, cancelacion);

        if (yaProcesado)
        {
            _logger.LogInformation("Mensaje duplicado descartado: {EventId} en {Cola}", sobre.EventId, NombreCola);
            return true;
        }

        EventoInventarioRecibido evento;
        try
        {
            evento = Interpretar(sobre);
        }
        catch (MensajeInventarioInvalidoException ex)
        {
            _logger.LogWarning(ex, "Mensaje invalido en la cola {Cola}; se hace ack sin aplicar.", NombreCola);
            return true;
        }

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        var manejador = scope.ServiceProvider.GetRequiredService<ManejadorEventosInventario>();

        try
        {
            var resultado = await manejador.ManejarAsync(evento, cancelacion);

            if (resultado == ResultadoEventoInventario.Aplicado)
            {
                MetricasSgo.MensajesConsumidosTotal.Inc();
            }

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
                "Fallo transitorio procesando {TipoEvento} {MessageId} en {Cola}; se nackea con requeue.",
                evento.TipoEvento,
                evento.MessageId,
                NombreCola);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }

    private static EventoInventarioRecibido Interpretar(IntegrationEvent<JsonElement> sobre)
    {
        return sobre.EventType switch
        {
            InventarioReservado.EventType => CrearEvento(sobre, Leer<InventarioReservadoData>(sobre.Data)),
            ReservaInventarioRechazada.EventType => CrearEvento(sobre, Leer<ReservaInventarioRechazadaData>(sobre.Data)),
            ReservaInventarioExpirada.EventType => CrearEvento(sobre, Leer<ReservaInventarioExpiradaData>(sobre.Data)),
            _ => throw new MensajeInventarioInvalidoException(
                $"Tipo de evento no reconocido: {sobre.EventType}")
        };
    }

    private static EventoInventarioRecibido CrearEvento<TData>(
        IntegrationEvent<JsonElement> sobre,
        TData datos)
        where TData : notnull
    {
        var ordenId = typeof(TData) switch
        {
            Type t when t == typeof(InventarioReservadoData) =>
                ((InventarioReservadoData)(object)datos).OrdenId,
            Type t when t == typeof(ReservaInventarioRechazadaData) =>
                ((ReservaInventarioRechazadaData)(object)datos).OrdenId,
            Type t when t == typeof(ReservaInventarioExpiradaData) =>
                ((ReservaInventarioExpiradaData)(object)datos).OrdenId,
            _ => sobre.AggregateId
        };

        return new EventoInventarioRecibido(
            MessageId: sobre.EventId,
            TransactionId: sobre.TransactionId,
            TipoEvento: sobre.EventType,
            OrdenId: ordenId,
            Datos: datos);
    }

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new MensajeInventarioInvalidoException($"El campo data del evento no coincide con {typeof(TData).Name}.");
}

/// <summary>
/// Sobre bien formado pero con contenido que Ordenes no puede aplicar (data vacio o
/// con forma incorrecta). Se trata como mensaje descartable: warning y ack.
/// </summary>
internal sealed class MensajeInventarioInvalidoException : Exception
{
    public MensajeInventarioInvalidoException(string mensaje)
        : base(mensaje)
    {
    }
}
