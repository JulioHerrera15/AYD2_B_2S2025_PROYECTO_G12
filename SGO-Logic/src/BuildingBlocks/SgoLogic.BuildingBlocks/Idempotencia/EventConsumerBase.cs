using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.Idempotencia;

/// <summary>
/// Consumidor base con deduplicacion por eventId y propagacion de correlacion.
/// El publicador de outbox entrega al menos una vez: todo lo que hereda de aca
/// obtiene la deduplicacion gratis, sin reescribirla.
///
/// Al arrancar declara su propia cola (quorum, con dead letter) segun lo que el
/// subtipo defina en Cola/Bindings, y se suscribe con ack manual. Por cada mensaje:
/// deserializa el envelope, si el eventId ya esta en mensajes_procesados hace ack
/// y descarta sin llamar a ManejarAsync; si no, puebla EventContext con el
/// transactionId recibido y el eventId como causationId, ejecuta ManejarAsync y
/// guarda su resultado junto con la marca de procesado en un solo SaveChangesAsync.
/// </summary>
public abstract class EventConsumerBase<TDbContext, TData> : BackgroundService
    where TDbContext : DbContext
    where TData : notnull
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger _logger;

    private IDisposable? _suscripcion;

    protected EventConsumerBase(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _conexion = conexion;
        _suscriptor = suscriptor;
        _logger = logger;
    }

    /// <summary>Nombre de la cola propia de este consumidor.</summary>
    protected abstract string Cola { get; }

    /// <summary>
    /// Identifica a este servicio dentro de mensajes_procesados. Junto con el
    /// eventId forma la clave de deduplicacion.
    /// </summary>
    protected abstract string NombreServicio { get; }

    /// <summary>Bindings de la cola propia hacia los exchanges de los que consume.</summary>
    protected abstract IReadOnlyCollection<BindingCola> Bindings { get; }

    protected virtual int LimiteReintentos => TopologiaMensajeria.LimiteReintentosPorDefecto;

    /// <summary>
    /// Logica propia del consumidor. Recibe el mismo TDbContext en el que, al
    /// volver, se va a insertar la marca de procesado y guardar todo junto: si
    /// necesita escribir algo (incluido encolar un evento derivado via
    /// IOutboxWriter resuelto en el mismo scope), no debe llamar a
    /// SaveChangesAsync por su cuenta.
    /// </summary>
    protected abstract Task ManejarAsync(IntegrationEvent<TData> evento, TDbContext contexto, IServiceProvider servicios, CancellationToken cancelacion);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var canal = _conexion.ObtenerConexion().CreateModel())
        {
            DeclaradorTopologia.DeclararEn(canal);
            DeclaradorColas.DeclararCola(canal, Cola, Bindings, LimiteReintentos);
        }

        _suscripcion = _suscriptor.Suscribir(Cola, cuerpo => ProcesarAsync(cuerpo, stoppingToken));
        stoppingToken.Register(() => _suscripcion?.Dispose());

        return Task.CompletedTask;
    }

    private async Task<bool> ProcesarAsync(ReadOnlyMemory<byte> cuerpo, CancellationToken cancelacion)
    {
        IntegrationEvent<TData>? evento;
        try
        {
            evento = JsonSerializer.Deserialize<IntegrationEvent<TData>>(cuerpo.Span, SgoJsonSerializerOptions.Default);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mensaje no deserializable en la cola {Cola}", Cola);
            return false;
        }

        if (evento is null)
        {
            _logger.LogError("Mensaje vacio en la cola {Cola}", Cola);
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var yaProcesado = await contexto.Set<MensajeProcesado>()
            .AnyAsync(m => m.EventId == evento.EventId && m.NombreServicio == NombreServicio, cancelacion);

        if (yaProcesado)
        {
            MetricasSgo.MensajesDuplicadosTotal.Inc();
            _logger.LogInformation("Mensaje duplicado descartado: {EventId} en {Cola}", evento.EventId, Cola);
            return true;
        }

        EventContext.TransactionId = evento.TransactionId;
        EventContext.CausationId = evento.EventId;

        try
        {
            await ManejarAsync(evento, contexto, scope.ServiceProvider, cancelacion);

            contexto.Set<MensajeProcesado>().Add(new MensajeProcesado(evento.EventId, NombreServicio, DateTime.UtcNow));
            await contexto.SaveChangesAsync(cancelacion);

            MetricasSgo.MensajesConsumidosTotal.Inc();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando el mensaje {EventId} en {Cola}", evento.EventId, Cola);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }
}