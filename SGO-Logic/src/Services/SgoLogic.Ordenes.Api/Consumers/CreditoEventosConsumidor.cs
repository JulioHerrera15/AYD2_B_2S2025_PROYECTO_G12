using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Consumers;

/// <summary>
/// Consume la cola <c>ordenes.credito</c> (bindings a <c>credito.evaluacion.aprobada</c>,
/// <c>credito.evaluacion.rechazada</c> y <c>credito.devolucion.aplicada</c>) y avanza la
/// ruta de credito de la saga con <see cref="ManejadorEventosCredito" />. Mismo patron que
/// <see cref="PagosEventosConsumidor" />: cola quorum con dead letter, deduplicacion
/// por eventId en <c>ordenes.mensaje_procesado</c> y correlacion via
/// <see cref="EventContext" />.
///
/// Politica de errores: transicion invalida o evento desconocido, ack con warning;
/// fallo transitorio (base, broker, choque de concurrencia con el timeout), nack con
/// requeue.
/// </summary>
public sealed class CreditoEventosConsumidor : BackgroundService
{
    public const string NombreCola = "ordenes.credito";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CreditoAprobado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CreditoRechazado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DevolucionCreditoAplicada.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<CreditoEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public CreditoEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<CreditoEventosConsumidor> logger)
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
        EventoCreditoRecibido evento;
        try
        {
            evento = Interpretar(cuerpo.Span);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mensaje no deserializable en la cola {Cola}; se nackea con requeue.", NombreCola);
            return false;
        }
        catch (MensajeCreditoInvalidoException ex)
        {
            _logger.LogWarning(ex, "Mensaje invalido en la cola {Cola}; se hace ack sin aplicar.", NombreCola);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var manejador = scope.ServiceProvider.GetRequiredService<ManejadorEventosCredito>();

        EventContext.TransactionId = evento.TransactionId;
        EventContext.CausationId = evento.MessageId;

        try
        {
            var resultado = await manejador.ManejarAsync(evento, cancelacion);
            if (resultado == ResultadoEventoCredito.Aplicado)
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
                evento.TipoEvento, evento.MessageId, NombreCola);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }

    private static EventoCreditoRecibido Interpretar(ReadOnlySpan<byte> cuerpo)
    {
        var sobre = JsonSerializer.Deserialize<IntegrationEvent<JsonElement>>(cuerpo, SgoJsonSerializerOptions.Default)
            ?? throw new MensajeCreditoInvalidoException("El sobre del evento de Credito llego vacio.");

        switch (sobre.EventType)
        {
            case CreditoAprobado.EventType:
            {
                var datos = Leer<CreditoAprobadoData>(sobre.Data);
                return new EventoCreditoRecibido(sobre.EventId, sobre.TransactionId, sobre.EventType, datos.OrdenId, CargoId: datos.CargoId);
            }

            case CreditoRechazado.EventType:
            {
                var datos = Leer<CreditoRechazadoData>(sobre.Data);
                return new EventoCreditoRecibido(
                    sobre.EventId, sobre.TransactionId, sobre.EventType, datos.OrdenId,
                    CodigoMotivo: datos.CodigoMotivo, MensajeCliente: datos.MensajeCliente);
            }

            case DevolucionCreditoAplicada.EventType:
            {
                var datos = Leer<DevolucionCreditoAplicadaData>(sobre.Data);
                return new EventoCreditoRecibido(
                    sobre.EventId, sobre.TransactionId, sobre.EventType, datos.OrdenOriginalId, DevolucionId: datos.DevolucionId);
            }

            default:
                return new EventoCreditoRecibido(sobre.EventId, sobre.TransactionId, sobre.EventType, sobre.AggregateId);
        }
    }

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new MensajeCreditoInvalidoException($"El campo data del evento no coincide con {typeof(TData).Name}.");
}

/// <summary>Sobre bien formado pero con data que Ordenes no puede aplicar: warning y ack.</summary>
internal sealed class MensajeCreditoInvalidoException : Exception
{
    public MensajeCreditoInvalidoException(string mensaje)
        : base(mensaje)
    {
    }
}
