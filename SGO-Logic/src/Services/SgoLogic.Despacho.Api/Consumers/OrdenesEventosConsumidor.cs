using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Despacho.Api.Application.Despacho;
using SgoLogic.Despacho.Api.Infrastructure.Persistencia;

namespace SgoLogic.Despacho.Api.Consumers;

/// <summary>
/// Consume la cola <c>despacho.ordenes</c> con bindings a
/// <c>ordenes.despacho.solicitado</c>, <c>ordenes.orden.metodo-entrega-modificado</c>
/// y <c>ordenes.orden.cancelada</c>.
///
/// No usa <c>EventConsumerBase</c> porque esa clase esta tipada a un unico
/// <c>TData</c> y aqui llegan tres. Sigue el patron de
/// <c>PagosEventosConsumidor</c> de Ordenes: cola quorum con dead letter, despacho
/// por <c>eventType</c>, deduplicacion a mano por <c>eventId</c> y propagacion de
/// correlacion via <see cref="EventContext" />.
/// </summary>
public sealed class OrdenesEventosConsumidor : BackgroundService
{
    public const string NombreCola = "despacho.ordenes";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DespachoSolicitado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, MetodoEntregaModificado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenCancelada.RoutingKey)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConexionRabbitMq _conexion;
    private readonly ISuscriptorMensajes _suscriptor;
    private readonly ILogger<OrdenesEventosConsumidor> _logger;

    private IDisposable? _suscripcion;

    public OrdenesEventosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<OrdenesEventosConsumidor> logger)
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
        IntegrationEvent<JsonElement> sobre;
        try
        {
            sobre = JsonSerializer.Deserialize<IntegrationEvent<JsonElement>>(cuerpo.Span, SgoJsonSerializerOptions.Default)
                ?? throw new JsonException("El sobre llego vacio.");
        }
        catch (JsonException ex)
        {
            // Sobre ilegible: nack para que x-delivery-limit lo mande a la dead letter
            // en vez de perderlo con un ack.
            _logger.LogError(ex, "Mensaje no deserializable en {Cola}; se nackea.", NombreCola);
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var proveedor = scope.ServiceProvider;
        var contexto = proveedor.GetRequiredService<DespachoDbContext>();
        var servicio = proveedor.GetRequiredService<ServicioDespacho>();
        var unidadDeTrabajo = proveedor.GetRequiredService<IUnidadDeTrabajo>();

        // Deduplicacion por eventId: al salir de EventConsumerBase hay que llevarla a
        // mano. La marca se guarda en el MISMO SaveChanges que el efecto de negocio,
        // asi que o quedan las dos cosas o ninguna.
        if (await contexto.MensajesProcesados.AnyAsync(m => m.EventId == sobre.EventId, cancelacion))
        {
            _logger.LogInformation("Evento {EventId} ya procesado; se descarta.", sobre.EventId);
            return true;
        }

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        try
        {
            var aplicado = await AplicarAsync(servicio, sobre, cancelacion);

            if (!aplicado)
            {
                _logger.LogWarning("Evento {Tipo} no esperado en {Cola}; ack sin aplicar.", sobre.EventType, NombreCola);
                return true;
            }

            contexto.MensajesProcesados.Add(new MensajeProcesado(sobre.EventId, NombreCola, DateTime.UtcNow));
            await unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

            return true;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Fallo transitorio (Inventario caido, base, choque de version): nack para
            // que la cola lo reintente.
            _logger.LogError(ex, "Fallo procesando {Tipo} {EventId}; se nackea con requeue.", sobre.EventType, sobre.EventId);
            return false;
        }
        finally
        {
            EventContext.TransactionId = null;
            EventContext.CausationId = null;
        }
    }

    private static async Task<bool> AplicarAsync(
        ServicioDespacho servicio,
        IntegrationEvent<JsonElement> sobre,
        CancellationToken cancelacion)
    {
        switch (sobre.EventType)
        {
            case DespachoSolicitado.EventType:
            {
                var datos = Leer<DespachoSolicitadoData>(sobre.Data);

                await servicio.SolicitarAsync(
                    new SolicitudDespacho(
                        datos.OrdenId,
                        datos.ReservaId,
                        datos.MetodoEntrega,
                        datos.UbicacionOrigenSugerida?.Id,
                        datos.UbicacionOrigenSugerida?.Tipo,
                        datos.UbicacionRecogidaId,
                        FormatearDireccion(datos.DireccionEntrega),
                        datos.Lineas.Select(l => new LineaDespachoDto(l.Sku, l.Descripcion, l.Cantidad)).ToList()),
                    cancelacion);

                return true;
            }

            case MetodoEntregaModificado.EventType:
            {
                var datos = Leer<MetodoEntregaModificadoData>(sobre.Data);

                await servicio.CambiarMetodoEntregaAsync(
                    new SolicitudCambioMetodo(
                        datos.OrdenId,
                        datos.MetodoEntregaNuevo,
                        datos.UbicacionRecogidaId,
                        FormatearDireccion(datos.DireccionEntrega)),
                    cancelacion);

                return true;
            }

            case OrdenCancelada.EventType:
            {
                var datos = Leer<OrdenCanceladaData>(sobre.Data);
                await servicio.AnularPorOrdenCanceladaAsync(datos.OrdenId, cancelacion);
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Aplana la direccion estructurada a una linea, que es lo que LogiExpress recibe.
    /// </summary>
    private static string? FormatearDireccion(DireccionEntregaData? direccion) =>
        direccion is null
            ? null
            : string.Join(", ", new[]
                {
                    direccion.Direccion,
                    direccion.Referencia,
                    direccion.Municipio,
                    direccion.Departamento
                }.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new JsonException($"El campo data no coincide con {typeof(TData).Name}.");
}
