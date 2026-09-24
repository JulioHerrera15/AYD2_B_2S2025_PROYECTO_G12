using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Consumers;

/// <summary>
/// Consume la cola <c>ordenes.pagos</c> (bindings a <c>pagos.pago.aplicado</c>,
/// <c>pagos.pago.pendiente</c>, <c>pagos.pago.rechazado</c>,
/// <c>pagos.reembolso.aplicado</c> y <c>pagos.reembolso.fallido</c>) y avanza la
/// saga de la orden con <see cref="ManejadorEventosPagos" />.
///
/// No usa <c>EventConsumerBase</c> porque esta cola recibe multiples <c>data</c>
/// distintos sobre un solo binding-set y la clase base esta tipada a un unico
/// <c>TData</c>. Aun asi replica su contrato: declara una cola quorum con dead
/// letter, deduplica por <c>eventId</c> antes de aplicar, y puebla
/// <see cref="EventContext" /> para que el evento derivado herede
/// <c>transactionId</c> y <c>causationId</c>.
///
/// Politica de errores (TOPOLOGIA-MENSAJERIA seccion 4):
/// <list type="bullet">
///   <item>Transicion invalida o evento desconocido: warning y ACK, sin requeue.</item>
///   <item>Fallo transitorio (base caida, broker, orden aun no visible): NACK con
///   requeue, para que <c>x-delivery-limit</c> y el dead-letter de HU-008 actuen.</item>
///   <item>Un mensaje malo nunca tumba el proceso.</item>
/// </list>
/// </summary>
public sealed class PagosEventosConsumidor : BackgroundService
{
    /// <summary>Cola propia del contexto Ordenes para los resultados de Pagos.</summary>
    public const string NombreCola = "ordenes.pagos";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, PagoAplicado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, PagoPendiente.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, PagoRechazado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReembolsoAplicado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReembolsoFallido.RoutingKey)
    };

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
        EventoPagoRecibido evento;
        try
        {
            evento = Interpretar(cuerpo.Span);
        }
        catch (JsonException ex)
        {
            // Sobre ilegible: se nackea para que x-delivery-limit lo cuente y la
            // cola quorum lo mande sola a sgo.eventos.dlx (evidencia para HU-008),
            // en vez de perderlo con un ack.
            _logger.LogError(ex, "Mensaje no deserializable en la cola {Cola}; se nackea con requeue.", NombreCola);
            return false;
        }
        catch (MensajePagoInvalidoException ex)
        {
            _logger.LogWarning(ex, "Mensaje invalido en la cola {Cola}; se hace ack sin aplicar.", NombreCola);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var manejador = scope.ServiceProvider.GetRequiredService<ManejadorEventosPagos>();

        // Correlacion: el evento que emita Ordenes hereda el transactionId entrante
        // y toma el eventId entrante como causa (envelope, seccion 4).
        EventContext.TransactionId = evento.TransactionId;
        EventContext.CausationId = evento.MessageId;

        try
        {
            var resultado = await manejador.ManejarAsync(evento, cancelacion);
            // Aplicado, Duplicado e Ignorado son todos ack; la metrica de consumo
            // exitoso solo cuenta Aplicado (no duplicados ni mensajes descartados
            // sin procesar), igual que FacturacionEventosConsumidor.
            if (resultado == ResultadoEventoPago.Aplicado)
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

    private static EventoPagoRecibido Interpretar(ReadOnlySpan<byte> cuerpo)
    {
        var sobre = JsonSerializer.Deserialize<IntegrationEvent<JsonElement>>(cuerpo, SgoJsonSerializerOptions.Default)
            ?? throw new MensajePagoInvalidoException("El sobre del evento de Pagos llego vacio.");

        return sobre.EventType switch
        {
            PagoAplicado.EventType => CrearAplicado(sobre, Leer<PagoAplicadoData>(sobre.Data)),

            PagoRechazado.EventType => CrearRechazado(sobre, Leer<PagoRechazadoData>(sobre.Data)),

            PagoPendiente.EventType => CrearPendiente(sobre, Leer<PagoPendienteData>(sobre.Data)),

            ReembolsoAplicado.EventType => CrearReembolsoAplicado(sobre, Leer<ReembolsoAplicadoData>(sobre.Data)),

            ReembolsoFallido.EventType => CrearReembolsoFallido(sobre, Leer<ReembolsoFallidoData>(sobre.Data)),

            // Tipo no esperado en esta cola: se arma igual para que el manejador lo
            // registre como desconocido y se haga ack.
            _ => Crear(sobre, sobre.AggregateId, motivo: null)
        };
    }

    private static EventoPagoRecibido CrearAplicado(IntegrationEvent<JsonElement> sobre, PagoAplicadoData datos) =>
        Crear(sobre, datos.OrdenId, motivo: null, autorizacionId: datos.AutorizacionId);

    // Solo redacta el motivo: Ordenes trata todo PagoRechazado igual. Los codigos
    // TOKEN_* (pagos-eventos-v1 v1.2) son un problema con el token del medio de pago,
    // no un rechazo del proveedor, y con TOKEN_REQUERIDO Pagos no llega a llamar al proveedor.
    private static EventoPagoRecibido CrearRechazado(IntegrationEvent<JsonElement> sobre, PagoRechazadoData datos) =>
        Crear(
            sobre,
            datos.OrdenId,
            datos.CodigoRespuesta.StartsWith("TOKEN_", StringComparison.Ordinal)
                ? $"Cobro rechazado por un problema con el token del medio de pago (codigo {datos.CodigoRespuesta})"
                : $"Cobro rechazado por el proveedor (codigo {datos.CodigoRespuesta})");

    private static EventoPagoRecibido CrearPendiente(IntegrationEvent<JsonElement> sobre, PagoPendienteData datos) =>
        Crear(sobre, datos.OrdenId, datos.MotivoFalla);

    private static EventoPagoRecibido CrearReembolsoAplicado(IntegrationEvent<JsonElement> sobre, ReembolsoAplicadoData datos) =>
        Crear(sobre, datos.OrdenOriginalId, motivo: null, autorizacionId: datos.AutorizacionId, devolucionId: datos.DevolucionId);

    private static EventoPagoRecibido CrearReembolsoFallido(IntegrationEvent<JsonElement> sobre, ReembolsoFallidoData datos) =>
        Crear(sobre, datos.OrdenOriginalId, datos.MotivoFalla, devolucionId: datos.DevolucionId);

    private static EventoPagoRecibido Crear(
        IntegrationEvent<JsonElement> sobre, string ordenId, string? motivo, string? autorizacionId = null, string? devolucionId = null) =>
        new(
            MessageId: sobre.EventId,
            TransactionId: sobre.TransactionId,
            TipoEvento: sobre.EventType,
            OrdenId: ordenId,
            Motivo: motivo,
            AutorizacionId: autorizacionId,
            DevolucionId: devolucionId);

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new MensajePagoInvalidoException($"El campo data del evento no coincide con {typeof(TData).Name}.");
}

/// <summary>
/// Sobre bien formado pero con contenido que Ordenes no puede aplicar (data vacio o
/// con forma incorrecta). Se trata como mensaje descartable: warning y ack.
/// </summary>
internal sealed class MensajePagoInvalidoException : Exception
{
    public MensajePagoInvalidoException(string mensaje)
        : base(mensaje)
    {
    }
}
