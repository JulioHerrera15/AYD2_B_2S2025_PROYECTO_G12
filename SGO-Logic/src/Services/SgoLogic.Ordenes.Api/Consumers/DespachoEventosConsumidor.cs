using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Consumers;

/// <summary>
/// Consume la cola <c>ordenes.despacho</c> (bindings a
/// <c>despacho.estado.actualizado</c> y <c>despacho.pedido.listo-para-recoger</c>
/// sobre <c>sgo.eventos</c>) y avanza la orden por el tramo de entrega con
/// <see cref="ManejadorEventosDespacho" />. Sin esto la orden se queda en
/// DESPACHO_SOLICITADO para siempre.
///
/// Los otros eventos de Despacho (origen determinado, guia asignada, guia
/// pendiente, preparacion solicitada) no cambian el estado de la orden segun
/// despacho-eventos-v1, asi que no se ligan a esta cola.
///
/// No usa <c>EventConsumerBase</c> por la misma razon que el resto de consumidores
/// de Ordenes: la cola recibe mas de un <c>data</c> y la clase base esta tipada a
/// un unico <c>TData</c>. Aun asi replica su contrato: cola quorum con dead
/// letter y <see cref="EventContext" /> poblado para que los eventos derivados
/// hereden <c>transactionId</c> y <c>causationId</c>. La deduplicacion por
/// <c>eventId</c> vive en el manejador, sobre <c>ordenes.mensaje_procesado</c>: la
/// marca se guarda en el mismo <c>SaveChangesAsync</c> que las transiciones y el
/// outbox, asi que o quedan todas o ninguna.
///
/// Politica de errores (misma que InventarioEventosConsumidor):
/// <list type="bullet">
///   <item>Estado sin equivalente, evento fuera de orden o contenido invalido:
///   ack sin aplicar y sin requeue.</item>
///   <item>Fallo transitorio (base caida, broker, choque de version): NACK con
///   requeue, para que <c>x-delivery-limit</c> y el dead-letter actuen.</item>
///   <item>Un mensaje malo nunca tumba el proceso.</item>
/// </list>
/// </summary>
public sealed class DespachoEventosConsumidor : BackgroundService
{
    /// <summary>Cola propia del contexto Ordenes para los eventos de Despacho.</summary>
    public const string NombreCola = "ordenes.despacho";

    private const string EstadoListaParaRecoger = "LISTA_PARA_RECOGER";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DespachoEstadoActualizado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, PedidoListoParaRecoger.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<DespachoEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public DespachoEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<DespachoEventosConsumidor> logger)
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
        EventoDespachoRecibido evento;
        try
        {
            evento = Interpretar(cuerpo.Span);
        }
        catch (JsonException ex)
        {
            // Sobre ilegible: nack para que x-delivery-limit lo cuente y la cola
            // quorum lo mande a sgo.eventos.dlx en vez de perderlo con un ack.
            _logger.LogError(ex, "Mensaje no deserializable en la cola {Cola}; se nackea con requeue.", NombreCola);
            return false;
        }
        catch (MensajeDespachoInvalidoException ex)
        {
            _logger.LogWarning(ex, "Mensaje invalido en la cola {Cola}; se hace ack sin aplicar.", NombreCola);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var manejador = scope.ServiceProvider.GetRequiredService<ManejadorEventosDespacho>();

        // Correlacion: los eventos que emita Ordenes heredan el transactionId
        // entrante y toman el eventId entrante como causa (envelope, seccion 4).
        EventContext.TransactionId = evento.TransactionId;
        EventContext.CausationId = evento.MessageId;

        try
        {
            var resultado = await manejador.ManejarAsync(evento, cancelacion);

            // Duplicado e Ignorado tambien hacen ack, pero la metrica de consumo
            // exitoso solo cuenta lo que realmente se aplico.
            if (resultado == ResultadoEventoDespacho.Aplicado)
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

    private static EventoDespachoRecibido Interpretar(ReadOnlySpan<byte> cuerpo)
    {
        var sobre = JsonSerializer.Deserialize<IntegrationEvent<JsonElement>>(cuerpo, SgoJsonSerializerOptions.Default)
            ?? throw new MensajeDespachoInvalidoException("El sobre del evento de Despacho llego vacio.");

        switch (sobre.EventType)
        {
            case DespachoEstadoActualizado.EventType:
            {
                var datos = Leer<DespachoEstadoActualizadoData>(sobre.Data);
                return new EventoDespachoRecibido(
                    sobre.EventId, sobre.TransactionId, sobre.EventType, datos.OrdenId, datos.EstadoNuevo, datos.Detalle);
            }

            case PedidoListoParaRecoger.EventType:
            {
                var datos = Leer<PedidoListoParaRecogerData>(sobre.Data);
                return new EventoDespachoRecibido(
                    sobre.EventId, sobre.TransactionId, sobre.EventType, datos.OrdenId, EstadoListaParaRecoger, null);
            }

            default:
                throw new MensajeDespachoInvalidoException($"Tipo de evento no reconocido: {sobre.EventType}");
        }
    }

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new MensajeDespachoInvalidoException($"El campo data del evento no coincide con {typeof(TData).Name}.");
}

/// <summary>
/// Sobre bien formado pero con contenido que Ordenes no puede aplicar (data vacio,
/// con forma incorrecta o tipo de evento desconocido). Se trata como mensaje
/// descartable: warning y ack.
/// </summary>
internal sealed class MensajeDespachoInvalidoException : Exception
{
    public MensajeDespachoInvalidoException(string mensaje)
        : base(mensaje)
    {
    }
}
