using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Infrastructure;

namespace SgoLogic.Notificaciones.Api.Consumers;

/// <summary>
/// Consumidor de la cola notificaciones.pagos.
/// Binding UNICO: pagos.pago.pendiente (evento PagoPendiente del contrato Pagos).
///
/// Razon de existir como consumidor SEPARADO:
/// La cola notificaciones.ordenes tiene topologia cerrada exclusiva a ordenes.orden.estado-cambiado
/// (enforzado por pruebas de arquitectura e integracion).
/// Se declara una cola dedicada "notificaciones.pagos" para escuchar eventos de cobro pendiente.
///
/// MANEJO AISLADO DE FALLOS (HU-015):
/// Si el proveedor de mensajeria (mock-mensajeria) falla, el correo queda en
/// notificacion_pendiente para reintento y el mensaje se confirma (ack).
/// La excepcion NUNCA se propaga fuera para evitar bloqueos y reintentos infinitos en RabbitMQ.
/// </summary>
public sealed class NotificacionesPagosConsumidor
    : EventConsumerBase<NotificacionesDbContext, PagoPendienteData>
{
    private const string BasePortalPorDefecto = "http://localhost:8092";

    public NotificacionesPagosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<NotificacionesPagosConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "notificaciones.pagos";

    protected override string NombreServicio => "notificaciones";

    // UN SOLO binding -- cola dedicada al evento de pago pendiente
    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, PagoPendiente.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<PagoPendienteData> evento,
        NotificacionesDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var basePortal = servicios.GetService<IConfiguration>()?["WebPortal:BaseUrl"] ?? BasePortalPorDefecto;
        var servicio = servicios.GetRequiredService<ServicioNotificaciones>();
        await servicio.NotificarPagoPendienteAsync(evento.Data, basePortal, evento.EventId, cancelacion);
    }
}
