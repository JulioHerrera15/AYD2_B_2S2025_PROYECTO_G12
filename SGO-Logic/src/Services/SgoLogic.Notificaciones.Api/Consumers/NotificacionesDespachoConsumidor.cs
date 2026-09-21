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
/// MANEJO AISLADO DE FALLOS: idéntico al consumidor de Órdenes — si el proveedor falla,
/// se loguea y se ackea. El aviso perdido no puede detener el flujo de Despacho.
/// </summary>
public sealed class NotificacionesDespachoConsumidor
    : EventConsumerBase<NotificacionesDbContext, PedidoListoParaRecogerData>
{
    private readonly ILogger<NotificacionesDespachoConsumidor> _logger;

    public NotificacionesDespachoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<NotificacionesDespachoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _logger = logger;
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
        var data = evento.Data;

        // Aislamiento de fallos: try/catch INTERNO — la excepción no debe salir de aquí
        try
        {
            var cliente = servicios.GetRequiredService<IMockMensajeriaClient>();

            // Reutilizamos la misma firma de EnviarAsync con estadoOrden = LISTA_PARA_RECOGER
            // para que el mock notifique al cliente que puede pasar por su pedido.
            await cliente.EnviarAsync(
                clienteId: data.OrdenId,   // usamos OrdenId como referencia; el mock lo maneja
                estadoOrden: "LISTA_PARA_RECOGER",
                ordenId: data.OrdenId,
                ct: cancelacion);

            _logger.LogInformation(
                "Notificación LISTA_PARA_RECOGER enviada para orden {OrdenId} en ubicación {UbicacionRecogidaId}",
                data.OrdenId, data.UbicacionRecogidaId);
        }
        catch (Exception ex)
        {
            // INTENCIONALMENTE no relanzamos. El ack se completará con éxito.
            // El aviso se pierde pero el flujo de Despacho no se afecta.
            _logger.LogWarning(
                ex,
                "Falla al enviar notificación LISTA_PARA_RECOGER para la orden {OrdenId} — aislado, evento marcado procesado",
                data.OrdenId);
        }
    }
}
