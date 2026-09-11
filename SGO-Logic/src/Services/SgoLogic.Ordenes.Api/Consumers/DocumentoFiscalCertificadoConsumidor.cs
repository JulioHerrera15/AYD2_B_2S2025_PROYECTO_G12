using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

namespace SgoLogic.Ordenes.Api.Consumers;

/// <summary>
/// Consume la cola <c>ordenes.facturacion</c>, ligada a los routing keys
/// <c>facturacion.documento-fiscal.certificado</c> y
/// <c>facturacion.documento-fiscal.certificacion-fallida</c> sobre
/// <c>sgo.eventos</c>. Maneja dos tipos de evento:
///
/// <list type="bullet">
///   <item><see cref="DocumentoFiscalCertificadoData" />: cierra el tramo
///   CONFIRMADA_PENDIENTE_FISCAL → CONFIRMADA (evento 1).</item>
///   <item><see cref="CertificacionSatFallidaData" />: levanta la bandera de
///   alerta fiscal cuando la SAT agoto los reintentos (evento 2).</item>
/// </list>
///
/// No usa <c>EventConsumerBase</c> porque esta cola recibe dos <c>data</c>
/// distintos sobre dos bindings y la clase base esta tipada a un unico
/// <c>TData</c>. Aun asi replica su contrato: cola quorum con dead letter,
/// deduplicacion por <c>eventId</c> antes de aplicar, y puebla
/// <see cref="EventContext" /> para que el evento derivado herede
/// <c>transactionId</c> y <c>causationId</c>.
/// </summary>
public sealed class DocumentoFiscalCertificadoConsumidor : Microsoft.Extensions.Hosting.BackgroundService
{
    public const string NombreCola = "ordenes.facturacion";
    private const string NombreServicio = "ordenes";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DocumentoFiscalCertificado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CertificacionSatFallida.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<DocumentoFiscalCertificadoConsumidor> _logger;

    private IDisposable? _suscripcion;

    public DocumentoFiscalCertificadoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<DocumentoFiscalCertificadoConsumidor> logger)
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

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        try
        {
            switch (sobre.EventType)
            {
                case DocumentoFiscalCertificado.EventType:
                {
                    var datos = sobre.Data.Deserialize<DocumentoFiscalCertificadoData>(SgoJsonSerializerOptions.Default)
                        ?? throw new InvalidOperationException("El campo data del evento no coincide con DocumentoFiscalCertificadoData.");
                    var manejador = scope.ServiceProvider.GetRequiredService<ManejadorDocumentoFiscalCertificado>();
                    await manejador.ManejarAsync(datos, cancelacion);
                    break;
                }

                case CertificacionSatFallida.EventType:
                {
                    var datos = sobre.Data.Deserialize<CertificacionSatFallidaData>(SgoJsonSerializerOptions.Default)
                        ?? throw new InvalidOperationException("El campo data del evento no coincide con CertificacionSatFallidaData.");
                    var manejador = scope.ServiceProvider.GetRequiredService<ManejadorCertificacionSatFallida>();
                    await manejador.ManejarAsync(datos, cancelacion);
                    break;
                }

                default:
                    _logger.LogWarning(
                        "Evento {EventType} no reconocido en la cola {Cola}; se confirma el mensaje.",
                        sobre.EventType, NombreCola);
                    break;
            }

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
}
