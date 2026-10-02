using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Infrastructure;

namespace SgoLogic.Notificaciones.Api.Consumers;

/// <summary>
/// Consumidor de la cola notificaciones.despacho.
/// Binding ÚNICO: despacho.pedido.listo-para-recoger (evento 6 del contrato Despacho).
///
/// Razón de existir como consumidor SEPARADO de NotificacionesOrdenesConsumidor:
/// La cola notificaciones.ordenes tiene binding CERRADO a ordenes.orden.estado-cambiado
/// y no debe recibir eventos de otros contextos (decisión de topología, ver instrucciones.txt
/// sección 4.1). Por eso se declara una nueva cola que atiende exclusivamente el aviso
/// al cliente de que puede pasar a recoger su pedido.
///
/// El destinatario es el ClienteId (email) que trae el evento, el mismo que Despacho
/// recibio en DespachoSolicitado; nunca el OrdenId.
///
/// MANEJO AISLADO DE FALLOS: idéntico al consumidor de Órdenes — si el proveedor falla,
/// el aviso queda en notificacion_pendiente y se ackea. El flujo de Despacho no se detiene.
/// </summary>
public sealed class NotificacionesDespachoConsumidor
    : EventConsumerBase<NotificacionesDbContext, PedidoListoParaRecogerData>
{
    public NotificacionesDespachoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<NotificacionesDespachoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "notificaciones.despacho";

    protected override string NombreServicio => "notificaciones";

    // UN SOLO binding — no agregar más sin consenso del equipo
    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, PedidoListoParaRecoger.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<PedidoListoParaRecogerData> evento,
        NotificacionesDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var servicio = servicios.GetRequiredService<ServicioNotificaciones>();
        await servicio.NotificarListoParaRecogerAsync(evento.Data, evento.EventId, cancelacion);
    }
}
