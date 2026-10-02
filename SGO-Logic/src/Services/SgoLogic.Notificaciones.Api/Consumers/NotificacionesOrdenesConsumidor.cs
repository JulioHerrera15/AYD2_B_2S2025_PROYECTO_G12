using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Infrastructure;

namespace SgoLogic.Notificaciones.Api.Consumers;

/// <summary>
/// Consumidor de la cola notificaciones.ordenes.
/// UNICO binding: ordenes.orden.estado-cambiado (evento 2).
/// Nunca se agrega otro binding a esta cola — decision de topologia cerrada
/// (ver docs/contratos/ordenes-eventos-v1.md seccion 4).
///
/// MANEJO AISLADO DE FALLOS: si el proveedor de mensajeria falla, la notificacion
/// se guarda en notificacion_pendiente (misma transaccion que la marca de procesado)
/// y se continua (ack). La excepcion NUNCA se propaga fuera de ManejarAsync para que
/// EventConsumerBase no haga nack: una notificacion fallida no puede fallar la saga
/// de Ordenes, y ya no se pierde porque el worker de reintentos la vuelve a enviar.
/// </summary>
public sealed class NotificacionesOrdenesConsumidor
    : EventConsumerBase<NotificacionesDbContext, OrdenEstadoCambiadoData>
{
    public NotificacionesOrdenesConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<NotificacionesOrdenesConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "notificaciones.ordenes";

    protected override string NombreServicio => "notificaciones";

    // UN SOLO binding — nunca agregar mas sin consenso del equipo
    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenEstadoCambiado.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<OrdenEstadoCambiadoData> evento,
        NotificacionesDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        // Aislamiento de fallos: si el proveedor falla, ServicioNotificaciones guarda la
        // notificacion como pendiente (en esta misma transaccion) y no relanza. El ack se
        // completa y la saga de Ordenes no se afecta; el worker la reintenta.
        var servicio = servicios.GetRequiredService<ServicioNotificaciones>();
        await servicio.NotificarEstadoOrdenAsync(evento.Data, evento.EventId, cancelacion);
    }
}
