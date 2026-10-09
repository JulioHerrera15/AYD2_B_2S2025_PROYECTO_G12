using Microsoft.Extensions.Logging;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Notificaciones.Api.Domain;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;
using SgoLogic.Notificaciones.Api.Kernel.Core;
using SgoLogic.Notificaciones.Api.Kernel.Plugins;

namespace SgoLogic.Notificaciones.Api.Application;

/// <summary>Un mensaje listo para los adaptadores del Microkernel.</summary>
public sealed record NotificacionSaliente(string Destinatario, string Asunto, string Cuerpo, string Canal = "EMAIL");

/// <summary>Puerto de persistencia de <see cref="NotificacionPendiente" />.</summary>
public interface IRepositorioNotificacionesPendientes
{
    /// <summary>Agrega sin guardar: el consumidor cierra la transaccion junto con la marca de procesado.</summary>
    void Agregar(NotificacionPendiente notificacion);

    /// <summary>Pendientes cuyo proximo reintento ya llego, las mas antiguas primero.</summary>
    Task<IReadOnlyList<NotificacionPendiente>> ListarParaReintentarAsync(DateTime ahora, int maximo, CancellationToken cancelacion);

    Task GuardarCambiosAsync(CancellationToken cancelacion);
}

/// <summary>
/// Fachada de alto nivel para notificación al cliente.
/// En la Fase 3, orquesta el despacho a través del Microkernel (<see cref="INotificacionesKernel"/>)
/// permitiendo el envío multicanal desacoplado (Email, SMS, Push, Webhooks) con aislamiento de fallas.
/// </summary>
public sealed class ServicioNotificaciones
{
    public const string CanalEmail = "EMAIL";
    public const string CanalSms = "SMS";
    public const string CanalPush = "PUSH";
    public const string CanalWebhook = "WEBHOOK";

    private const int LoteReintentos = 50;

    private readonly IMockMensajeriaClient _mensajeria;
    private readonly IRepositorioNotificacionesPendientes _pendientes;
    private readonly ILogger<ServicioNotificaciones> _logger;
    private readonly INotificacionesKernel _kernel;
    private readonly Func<DateTime> _reloj;

    public ServicioNotificaciones(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<ServicioNotificaciones> logger,
        INotificacionesKernel kernel)
        : this(mensajeria, pendientes, logger, kernel, () => DateTime.UtcNow)
    {
    }

    public ServicioNotificaciones(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<ServicioNotificaciones> logger,
        INotificacionesKernel kernel,
        Func<DateTime> reloj)
    {
        _mensajeria = mensajeria;
        _pendientes = pendientes;
        _logger = logger;
        _kernel = kernel;
        _reloj = reloj;
    }

    /// <summary>Constructor para compatibilidad con suites de pruebas unitarias existentes.</summary>
    public ServicioNotificaciones(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<ServicioNotificaciones> logger)
        : this(mensajeria, pendientes, logger, CrearKernelPorDefecto(mensajeria, pendientes, () => DateTime.UtcNow), () => DateTime.UtcNow)
    {
    }

    public ServicioNotificaciones(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<ServicioNotificaciones> logger,
        Func<DateTime> reloj)
        : this(mensajeria, pendientes, logger, CrearKernelPorDefecto(mensajeria, pendientes, reloj), reloj)
    {
    }

    public Task NotificarEstadoOrdenAsync(OrdenEstadoCambiadoData data, string eventId, CancellationToken cancelacion) =>
        EnviarOGuardarAsync(
            new NotificacionSaliente(
                data.ClienteId,
                $"Tu orden {data.OrdenId} cambio de estado",
                $"El estado de tu orden {data.OrdenId} es ahora: {data.EstadoNuevo}"),
            eventId,
            cancelacion);

    public Task NotificarListoParaRecogerAsync(PedidoListoParaRecogerData data, string eventId, CancellationToken cancelacion)
    {
        if (string.IsNullOrWhiteSpace(data.ClienteId))
        {
            _logger.LogWarning(
                "PedidoListoParaRecoger de la orden {OrdenId} sin ClienteId: no hay destinatario, el aviso no se envia.",
                data.OrdenId);
            return Task.CompletedTask;
        }

        return EnviarOGuardarAsync(
            new NotificacionSaliente(
                data.ClienteId,
                $"Tu pedido {data.OrdenId} esta listo para recoger",
                $"Tu pedido {data.OrdenId} ya esta listo. Puedes pasar por el en la tienda {data.UbicacionRecogidaId} presentando tu numero de orden."),
            eventId,
            cancelacion);
    }

    public Task NotificarPagoPendienteAsync(
        PagoPendienteData data,
        string basePortal,
        string eventId,
        CancellationToken cancelacion)
    {
        var expiraEn = data.EnlaceReintento?.ExpiraEn.ToString("u") ?? "N/A";
        var urlReintento = ResolverUrlReintento(data.EnlaceReintento, basePortal);

        return EnviarOGuardarAsync(
            new NotificacionSaliente(
                data.ClienteId,
                $"Pago pendiente para tu orden {data.OrdenId}",
                $"Tu pago de {data.Moneda} {data.Monto:F2} para la orden {data.OrdenId} no pudo completarse en este momento. " +
                "Tu pedido sigue registrado y no se realizo ningun cobro. " +
                $"Puedes completar tu pago utilizando el siguiente enlace seguro: {urlReintento} (Valido hasta: {expiraEn})."),
            eventId,
            cancelacion);
    }

    /// <summary>
    /// Despacha la notificación a través del Microkernel.
    /// Si un plugin falla, el Kernel aísla el error y persiste la notificación como pendiente.
    /// </summary>
    public async Task EnviarOGuardarAsync(NotificacionSaliente notificacion, string eventId, CancellationToken cancelacion)
    {
        var solicitud = new SolicitudNotificacion(
            EventId: eventId,
            Canal: notificacion.Canal,
            Destinatario: notificacion.Destinatario,
            Asunto: notificacion.Asunto,
            Cuerpo: notificacion.Cuerpo
        );

        await _kernel.DespacharAsync(solicitud, cancelacion);
    }

    /// <summary>
    /// Reintenta las pendientes cuyo turno llego invocando el plugin correspondiente en el Microkernel.
    /// </summary>
    public async Task<int> ReintentarPendientesAsync(CancellationToken cancelacion)
    {
        var pendientes = await _pendientes.ListarParaReintentarAsync(_reloj(), LoteReintentos, cancelacion);

        foreach (var pendiente in pendientes)
        {
            try
            {
                var resultado = await _kernel.DespacharReintentoAsync(
                    pendiente.Canal,
                    pendiente.Destinatario,
                    pendiente.Asunto,
                    pendiente.Cuerpo,
                    cancelacion);

                if (resultado.Exitoso)
                {
                    pendiente.MarcarEnviada(_reloj());
                    _logger.LogInformation(
                        "Notificacion pendiente {Id} (canal {Canal}, evento {EventId}) entregada en reintento {Intento}.",
                        pendiente.Id, pendiente.Canal, pendiente.EventId, pendiente.Intentos);
                }
                else
                {
                    pendiente.RegistrarFallo(resultado.Error, _reloj());
                    if (pendiente.Estado == EstadoNotificacionPendiente.FALLIDA)
                    {
                        _logger.LogError(
                            "Notificacion pendiente {Id} (canal {Canal}, evento {EventId}) FALLIDA tras {Intentos} reintentos.",
                            pendiente.Id, pendiente.Canal, pendiente.EventId, pendiente.Intentos);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
            {
                pendiente.RegistrarFallo(ex.Message, _reloj());
                if (pendiente.Estado == EstadoNotificacionPendiente.FALLIDA)
                {
                    _logger.LogError(
                        "Notificacion pendiente {Id} (evento {EventId}) FALLIDA tras excepcion: {Error}",
                        pendiente.Id, pendiente.EventId, ex.Message);
                }
            }
        }

        if (pendientes.Count > 0)
        {
            await _pendientes.GuardarCambiosAsync(cancelacion);
        }

        return pendientes.Count;
    }

    private static INotificacionesKernel CrearKernelPorDefecto(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        Func<DateTime>? reloj = null)
    {
        var registry = new PluginRegistry();
        registry.Registrar(new EmailNotificacionPlugin(mensajeria, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailNotificacionPlugin>.Instance));
        registry.Registrar(new SmsNotificacionPlugin(mensajeria, Microsoft.Extensions.Logging.Abstractions.NullLogger<SmsNotificacionPlugin>.Instance));
        registry.Registrar(new PushNotificacionPlugin(mensajeria, Microsoft.Extensions.Logging.Abstractions.NullLogger<PushNotificacionPlugin>.Instance));
        registry.Registrar(new WebhookNotificacionPlugin(mensajeria, Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookNotificacionPlugin>.Instance));

        return new NotificacionesKernel(
            registry,
            pendientes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificacionesKernel>.Instance,
            reloj ?? (() => DateTime.UtcNow));
    }

    private static string ResolverUrlReintento(EnlaceReintentoData? enlace, string basePortal)
    {
        var urlOriginal = enlace?.Url;

        if (!string.IsNullOrWhiteSpace(urlOriginal)
            && (urlOriginal.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || urlOriginal.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return urlOriginal;
        }

        if (!string.IsNullOrWhiteSpace(enlace?.Token))
        {
            return $"{basePortal.TrimEnd('/')}/Pagar/Reintentar?token={enlace.Token}";
        }

        return string.IsNullOrWhiteSpace(urlOriginal)
            ? "(enlace no disponible)"
            : $"{basePortal.TrimEnd('/')}/{urlOriginal.TrimStart('/')}";
    }
}
