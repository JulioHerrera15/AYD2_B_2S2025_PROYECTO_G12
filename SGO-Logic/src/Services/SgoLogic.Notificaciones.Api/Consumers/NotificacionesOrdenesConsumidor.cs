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
/// MANEJO AISLADO DE FALLOS: si el proveedor de mensajeria falla, se loguea
/// y se continua (ack). La excepcion NUNCA se propaga fuera de ManejarAsync
/// para que EventConsumerBase no haga nack y reintente — una notificacion perdida
/// no puede fallar la saga de Ordenes.
/// </summary>
public sealed class NotificacionesOrdenesConsumidor
    : EventConsumerBase<NotificacionesDbContext, OrdenEstadoCambiadoData>
{
    private readonly ILogger<NotificacionesOrdenesConsumidor> _logger;

    public NotificacionesOrdenesConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<NotificacionesOrdenesConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _logger = logger;
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
        var data = evento.Data;

        // Aislamiento de fallos: try/catch INTERNO — la excepcion no debe salir de aqui
        try
        {
            var cliente = servicios.GetRequiredService<IMockMensajeriaClient>();
            await cliente.EnviarAsync(
                clienteId: data.ClienteId,
                estadoOrden: data.EstadoNuevo.ToString(),
                ordenId: data.OrdenId,
                ct: cancelacion);
        }
        catch (Exception ex)
        {
            // INTENCIONALMENTE no relanzamos. El ack se completara con exito.
            // La notificacion se pierde pero la saga de Ordenes no se afecta.
            _logger.LogWarning(
                ex,
                "Falla al notificar al cliente {ClienteId} para la orden {OrdenId} " +
                "(estado: {Estado}) — aislado, el evento se marca procesado de todos modos",
                data.ClienteId, data.OrdenId, data.EstadoNuevo);
        }
    }
}
