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
/// Si el proveedor de mensajeria (mock-mensajeria) falla, la excepcion se captura internamente
/// en ManejarAsync, se registra como advertencia y se confirma (ack).
/// La excepcion NUNCA se propaga fuera para evitar bloqueos y reintentos infinitos en RabbitMQ.
/// </summary>
public sealed class NotificacionesPagosConsumidor
    : EventConsumerBase<NotificacionesDbContext, PagoPendienteData>
{
    private readonly ILogger<NotificacionesPagosConsumidor> _logger;

    public NotificacionesPagosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<NotificacionesPagosConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _logger = logger;
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
        var data = evento.Data;

        // Aislamiento de fallos: try/catch INTERNO -- la excepcion no debe salir de aqui
        try
        {
            var cliente = servicios.GetRequiredService<IMockMensajeriaClient>();
            var config = servicios.GetService<Microsoft.Extensions.Configuration.IConfiguration>();

            var asunto = $"Pago pendiente para tu orden {data.OrdenId}";
            var expiraEn = data.EnlaceReintento?.ExpiraEn.ToString("u") ?? "N/A";

            var basePortal = config?["WebPortal:BaseUrl"] ?? "http://localhost:8092";
            var urlOriginal = data.EnlaceReintento?.Url;
            string urlReintento;

            if (!string.IsNullOrWhiteSpace(urlOriginal) && (urlOriginal.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlOriginal.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                urlReintento = urlOriginal;
            }
            else if (!string.IsNullOrWhiteSpace(data.EnlaceReintento?.Token))
            {
                urlReintento = $"{basePortal.TrimEnd('/')}/Pagar/Reintentar?token={data.EnlaceReintento.Token}";
            }
            else if (!string.IsNullOrWhiteSpace(urlOriginal))
            {
                urlReintento = $"{basePortal.TrimEnd('/')}/{urlOriginal.TrimStart('/')}";
            }
            else
            {
                urlReintento = "(enlace no disponible)";
            }

            // El motivo tecnico (data.MotivoFalla) NO va al cliente: puede traer nombres de
            // host, del proveedor o errores de red. Queda en el intento de pago, en el log
            // de Pagos y en Auditoria, que consume este mismo evento.
            var cuerpo = $"Tu pago de {data.Moneda} {data.Monto:F2} para la orden {data.OrdenId} no pudo completarse en este momento. " +
                         "Tu pedido sigue registrado y no se realizo ningun cobro. " +
                         $"Puedes completar tu pago utilizando el siguiente enlace seguro: {urlReintento} (Valido hasta: {expiraEn}).";

            await cliente.EnviarEmailAsync(
                destinatario: data.ClienteId,
                asunto: asunto,
                cuerpo: cuerpo,
                ct: cancelacion);

            _logger.LogInformation(
                "Notificacion de pago pendiente enviada al cliente {ClienteId} para orden {OrdenId} con enlace {Url}",
                data.ClienteId, data.OrdenId, urlReintento);
        }
        catch (Exception ex)
        {
            // INTENCIONALMENTE no relanzamos. El ack se completara con exito.
            // La notificacion se pierde pero no bloquea la cola ni afecta el flujo de Pagos/Ordenes.
            _logger.LogWarning(
                ex,
                "Falla al notificar pago pendiente al cliente {ClienteId} para orden {OrdenId} -- aislado, evento marcado procesado",
                data.ClienteId, data.OrdenId);
        }
    }
}
