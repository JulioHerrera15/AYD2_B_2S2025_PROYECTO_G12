using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;

namespace SgoLogic.Inventario.Api.Consumers;

/// <summary>
/// Consume la cola <c>inventario.ordenes</c> con bindings a
/// <c>ordenes.reserva.solicitada</c> (HU-001),
/// <c>ordenes.reserva.liberacion-solicitada</c> (HU-003) y
/// <c>ordenes.devolucion.registrada</c> (HU-014).
///
/// Reemplaza al antiguo <c>ReservaSolicitadaConsumidor</c>, que heredaba de
/// <c>EventConsumerBase</c>: esa clase base esta tipada a un unico <c>TData</c> y
/// esta cola ahora recibe tres. Se sigue el patron que el equipo ya usa para colas
/// multi-evento (<c>PagosEventosConsumidor</c> en Ordenes): cola quorum con dead
/// letter, despacho por <c>eventType</c>, deduplicacion a mano por <c>eventId</c> y
/// propagacion de correlacion.
///
/// La cola y sus bindings no cambian de nombre, asi que la topologia declarada
/// sigue siendo la misma que describe el contrato.
///
/// Politica de errores (TOPOLOGIA-MENSAJERIA seccion 4):
/// <list type="bullet">
///   <item>Sobre ilegible: nack, para que x-delivery-limit lo mande a la dead letter.</item>
///   <item>Evento no esperado en esta cola: warning y ack, sin aplicar.</item>
///   <item>Fallo transitorio (base caida, choque de version): nack con requeue.</item>
/// </list>
/// </summary>
public sealed class OrdenesEventosConsumidor : BackgroundService
{
    public const string NombreCola = "inventario.ordenes";

    private static readonly IReadOnlyCollection<BindingCola> BindingsCola = new[]
    {
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReservaInventarioSolicitada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, LiberacionReservaSolicitada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DevolucionRegistrada.RoutingKey)
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
                ?? throw new JsonException("El sobre del evento llego vacio.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mensaje no deserializable en {Cola}; se nackea.", NombreCola);
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var servicios = scope.ServiceProvider;
        var contexto = servicios.GetRequiredService<InventarioDbContext>();
        var unidadDeTrabajo = servicios.GetRequiredService<IUnidadDeTrabajo>();

        // Deduplicacion por eventId, en el mismo SaveChanges que el efecto de
        // negocio: o quedan las dos cosas o ninguna.
        if (await contexto.MensajesProcesados.AnyAsync(m => m.EventId == sobre.EventId, cancelacion))
        {
            _logger.LogInformation("Evento {EventId} ya procesado; se descarta.", sobre.EventId);
            return true;
        }

        EventContext.TransactionId = sobre.TransactionId;
        EventContext.CausationId = sobre.EventId;

        try
        {
            if (!await AplicarAsync(servicios, sobre, cancelacion))
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
            _logger.LogError(
                ex,
                "Fallo procesando {Tipo} {EventId} en {Cola}; se nackea con requeue.",
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

    private static async Task<bool> AplicarAsync(
        IServiceProvider servicios,
        IntegrationEvent<JsonElement> sobre,
        CancellationToken cancelacion)
    {
        switch (sobre.EventType)
        {
            case ReservaInventarioSolicitada.EventType:
            {
                var datos = Leer<ReservaInventarioSolicitadaData>(sobre.Data);

                await servicios.GetRequiredService<ServicioReservas>().ReservarAsync(
                    new SolicitudReserva(
                        datos.OrdenId,
                        datos.MetodoEntrega,
                        datos.UbicacionPreferidaId,
                        datos.TtlReservaSegundos,
                        datos.Lineas.Select(l => new LineaSolicitudReserva(l.Sku, l.Cantidad)).ToList()),
                    cancelacion);

                return true;
            }

            case LiberacionReservaSolicitada.EventType:
            {
                // HU-003: compensacion de la saga. Sin esto, un cobro rechazado deja
                // el stock retenido hasta que venza el TTL.
                var datos = Leer<LiberacionReservaSolicitadaData>(sobre.Data);

                await servicios.GetRequiredService<ServicioLiberacionReservas>()
                    .LiberarAsync(datos.OrdenId, datos.Motivo, cancelacion);

                return true;
            }

            case DevolucionRegistrada.EventType:
            {
                // HU-014: las unidades devueltas vuelven al anaquel.
                var datos = Leer<DevolucionRegistradaData>(sobre.Data);

                await servicios.GetRequiredService<ServicioReingresoStock>().ReingresarAsync(
                    new SolicitudReingreso(
                        datos.DevolucionId,
                        datos.OrdenOriginalId,
                        datos.UbicacionReingresoId,
                        TipoUbicacion.TIENDA,
                        datos.Lineas
                            .Select(l => new LineaReingreso(l.Sku, l.Cantidad, l.UbicacionOrigenReservaId))
                            .ToList()),
                    cancelacion);

                return true;
            }

            default:
                return false;
        }
    }

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new JsonException($"El campo data del evento no coincide con {typeof(TData).Name}.");
}
