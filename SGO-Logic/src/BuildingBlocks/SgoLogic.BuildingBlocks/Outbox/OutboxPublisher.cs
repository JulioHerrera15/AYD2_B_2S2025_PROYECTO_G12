using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Metricas;

namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>
/// BackgroundService que hace polling sobre la tabla outbox de TDbContext y
/// publica lo pendiente. Se registra una instancia por servicio (via
/// AddOutbox&lt;TDbContext&gt;()), corriendo dentro del mismo proceso que ese
/// servicio, sobre su propia tabla.
///
/// Cada ciclo abre un scope (para tener su propio DbContext, ya que este es un
/// singleton) y un solo canal de RabbitMQ para todo el lote. Si el broker no esta
/// disponible al abrir el canal, el ciclo entero se aborta sin tocar ninguna fila
/// y se reintenta en el siguiente polling: eso es lo que hace que un POST
/// funcione con el broker caido. Si el broker esta disponible pero un mensaje en
/// particular no se confirma, ese mensaje (y solo ese) cuenta como un intento
/// fallido con backoff.
/// </summary>
public sealed class OutboxPublisher<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly OutboxOptions _opciones;
    private readonly ILogger<OutboxPublisher<TDbContext>> _logger;

    public OutboxPublisher(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        IOptions<OutboxOptions> opciones,
        ILogger<OutboxPublisher<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _conexion = conexion;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ciclo de OutboxPublisher termino con un error inesperado.");
            }

            await Task.Delay(_opciones.PollingIntervalMs, stoppingToken);
        }
    }

    private async Task EjecutarCicloAsync(CancellationToken cancelacion)
    {
        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<TDbContext>();

        MetricasSgo.OutboxPendientes.Set(
            await contexto.Set<OutboxMessage>().CountAsync(m => m.Estado == EstadoOutbox.Pendiente, cancelacion));

        // FOR UPDATE SKIP LOCKED solo protege mientras la transaccion que lo pidio
        // siga abierta: fuera de una transaccion explicita, Npgsql confirma la
        // sentencia SELECT sola y libera el bloqueo de inmediato, dejando a otra
        // instancia del publicador tomar las mismas filas. Por eso todo el ciclo
        // (leer, publicar, marcar) corre dentro de una unica transaccion que se
        // compromete recien al final.
        await using var transaccion = await contexto.Database.BeginTransactionAsync(cancelacion);

        var pendientes = await contexto.Set<OutboxMessage>()
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE estado = {0}
                  AND (proximo_intento_en IS NULL OR proximo_intento_en <= now())
                ORDER BY fecha_creacion
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                nameof(EstadoOutbox.Pendiente),
                _opciones.BatchSize)
            .ToListAsync(cancelacion);

        if (pendientes.Count == 0)
        {
            return;
        }

        IModel? canal = null;
        try
        {
            canal = _conexion.ObtenerConexion().CreateModel();
            DeclaradorTopologia.DeclararEn(canal);
            canal.ConfirmSelect();

            foreach (var mensaje in pendientes)
            {
                PublicarMensaje(canal, mensaje);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ no disponible; se reintenta en el proximo ciclo de polling.");
            return;
        }
        finally
        {
            canal?.Dispose();
        }

        await contexto.SaveChangesAsync(cancelacion);
        await transaccion.CommitAsync(cancelacion);
    }

    private void PublicarMensaje(IModel canal, OutboxMessage mensaje)
    {
        try
        {
            var cuerpo = System.Text.Encoding.UTF8.GetBytes(mensaje.Payload);
            PublicadorRabbitMq.PublicarEnCanalConfirmado(canal, mensaje.Exchange, mensaje.RoutingKey, cuerpo);

            var momento = DateTime.UtcNow;
            mensaje.MarcarPublicado(momento);

            MetricasSgo.OutboxPublicadosTotal.Inc();
            MetricasSgo.OutboxLatenciaPublicacionSegundos.Observe((momento - mensaje.FechaCreacion).TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo publicar el mensaje de outbox {EventId}; se reintentara.", mensaje.EventId);

            mensaje.RegistrarFallo(_opciones.MaxReintentos);

            if (mensaje.Estado == EstadoOutbox.Fallido)
            {
                EnviarADeadLetter(canal, mensaje);
            }
        }
    }

    private void EnviarADeadLetter(IModel canal, OutboxMessage mensaje)
    {
        try
        {
            var cuerpo = System.Text.Encoding.UTF8.GetBytes(mensaje.Payload);
            PublicadorRabbitMq.PublicarEnCanalConfirmado(canal, TopologiaMensajeria.ExchangeDeadLetter, string.Empty, cuerpo);

            _logger.LogError(
                "Mensaje de outbox {EventId} agoto sus reintentos y se envio a {DeadLetter}.",
                mensaje.EventId,
                TopologiaMensajeria.ExchangeDeadLetter);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "El mensaje {EventId} agoto reintentos pero no se pudo enviar a dead letter; queda Fallido para revision manual.",
                mensaje.EventId);
        }

        MetricasSgo.OutboxFallidosTotal.Inc();
    }
}