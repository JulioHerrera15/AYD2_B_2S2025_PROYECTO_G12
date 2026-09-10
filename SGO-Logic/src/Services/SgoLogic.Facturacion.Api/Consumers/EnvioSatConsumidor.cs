using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

namespace SgoLogic.Facturacion.Api.Consumers;

/// <summary>
/// Enviador a la SAT. Consume la cola dedicada <c>facturacion.sat</c>, ligada al
/// exchange direct <c>sgo.sat</c> con routing key <c>sat.certificacion</c>
/// (TOPOLOGIA-MENSAJERIA seccion 1, canal MOM interno de Facturacion). Por cada
/// solicitud de envio invoca a la SAT y aplica el desenlace en
/// <see cref="ServicioFacturacion" />.
///
/// No usa <c>EventConsumerBase</c> porque el mensaje viaja por <c>sgo.sat</c> y no
/// por <c>sgo.eventos</c>. Aun asi replica su contrato: cola quorum con dead
/// letter, deduplicacion por <c>eventId</c> en <c>mensajes_procesados</c> y
/// correlacion propagada a los eventos derivados.
///
/// Politica de errores: un rechazo de la SAT, un timeout o un 5xx son desenlaces
/// de negocio (backoff persistido) y se ACKean. Solo se NACKea ante un error
/// tecnico del propio lado (base caida).
/// </summary>
public sealed class EnvioSatConsumidor : BackgroundService
{
    public const string NombreCola = "facturacion.sat";
    private const string NombreServicio = "facturacion-sat";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeSat, SolicitudEnvioSat.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<EnvioSatConsumidor> _logger;

    private IDisposable? _suscripcion;

    public EnvioSatConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<EnvioSatConsumidor> logger)
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
        IntegrationEvent<SolicitudEnvioSatData>? sobre;
        try
        {
            sobre = JsonSerializer.Deserialize<IntegrationEvent<SolicitudEnvioSatData>>(
                cuerpo.Span, SgoJsonSerializerOptions.Default);
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
        var contexto = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();

        var yaProcesado = await contexto.Set<MensajeProcesado>()
            .AnyAsync(m => m.EventId == sobre.EventId && m.NombreServicio == NombreServicio, cancelacion);

        if (yaProcesado)
        {
            _logger.LogInformation("Solicitud de envio duplicada descartada: {EventId}.", sobre.EventId);
            return true;
        }

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        try
        {
            if (!Guid.TryParse(sobre.Data.DocumentoFiscalId, out var documentoId))
            {
                _logger.LogWarning(
                    "Solicitud de envio con id de documento invalido ({Id}); se ackea sin procesar.",
                    sobre.Data.DocumentoFiscalId);
                return true;
            }

            var servicio = scope.ServiceProvider.GetRequiredService<ServicioFacturacion>();
            await servicio.ProcesarEnvioAsync(documentoId, sobre.Data.Intento, DateTime.UtcNow, cancelacion);

            contexto.Set<MensajeProcesado>().Add(
                new MensajeProcesado(sobre.EventId, NombreServicio, DateTime.UtcNow));
            await contexto.SaveChangesAsync(cancelacion);

            return true;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Fallo tecnico enviando a la SAT el documento {DocumentoId}; se nackea con requeue.",
                sobre.Data.DocumentoFiscalId);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }
}
