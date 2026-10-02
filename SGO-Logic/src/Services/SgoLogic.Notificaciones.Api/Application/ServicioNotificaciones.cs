using Microsoft.Extensions.Logging;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Notificaciones.Api.Domain;

namespace SgoLogic.Notificaciones.Api.Application;

/// <summary>Un correo listo para el proveedor de mensajeria.</summary>
public sealed record NotificacionSaliente(string Destinatario, string Asunto, string Cuerpo);

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
/// Envio de notificaciones al cliente (B3). Arma cada correo a partir del evento y lo
/// manda al proveedor. Si el proveedor falla, la notificacion no se pierde ni rompe el
/// flujo de quien publico el evento: se guarda como pendiente (en la misma transaccion
/// que la marca de mensaje procesado) y <see cref="ReintentarPendientesAsync" /> la
/// reintenta con backoff.
/// </summary>
public sealed class ServicioNotificaciones
{
    public const string CanalEmail = "EMAIL";
    private const int LoteReintentos = 50;

    private readonly IMockMensajeriaClient _mensajeria;
    private readonly IRepositorioNotificacionesPendientes _pendientes;
    private readonly ILogger<ServicioNotificaciones> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioNotificaciones(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<ServicioNotificaciones> logger)
        : this(mensajeria, pendientes, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioNotificaciones(
        IMockMensajeriaClient mensajeria,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<ServicioNotificaciones> logger,
        Func<DateTime> reloj)
    {
        _mensajeria = mensajeria;
        _pendientes = pendientes;
        _logger = logger;
        _reloj = reloj;
    }

    public Task NotificarEstadoOrdenAsync(OrdenEstadoCambiadoData data, string eventId, CancellationToken cancelacion) =>
        EnviarOGuardarAsync(
            new NotificacionSaliente(
                data.ClienteId,
                $"Tu orden {data.OrdenId} cambio de estado",
                $"El estado de tu orden {data.OrdenId} es ahora: {data.EstadoNuevo}"),
            eventId,
            cancelacion);

    /// <summary>
    /// Aviso de pedido listo para recoger al email del cliente (ClienteId del evento,
    /// que Despacho trae desde DespachoSolicitado). Un evento sin ClienteId (despachos
    /// creados antes de que el contrato lo llevara) no tiene a quien avisar: se registra
    /// y no se envia nada. Nunca se usa el OrdenId como destinatario.
    /// </summary>
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

    /// <summary>
    /// Correo con el enlace de reintento de un pago pendiente. El motivo tecnico
    /// (data.MotivoFalla) NO va al cliente: puede traer nombres de host, del proveedor o
    /// errores de red. Queda en el intento de pago, en el log de Pagos y en Auditoria.
    /// </summary>
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
    /// Intenta el envio. Si el proveedor falla agrega una <see cref="NotificacionPendiente" />
    /// sin guardar (lo guarda la transaccion del consumidor) y no relanza: el evento se
    /// marca procesado igual y el flujo de origen no se afecta.
    /// </summary>
    public async Task EnviarOGuardarAsync(NotificacionSaliente notificacion, string eventId, CancellationToken cancelacion)
    {
        try
        {
            await _mensajeria.EnviarEmailAsync(notificacion.Destinatario, notificacion.Asunto, notificacion.Cuerpo, cancelacion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
        {
            _pendientes.Agregar(new NotificacionPendiente(
                eventId,
                CanalEmail,
                notificacion.Destinatario,
                notificacion.Asunto,
                notificacion.Cuerpo,
                ex.Message,
                _reloj()));

            _logger.LogWarning(
                ex,
                "Fallo el envio de la notificacion del evento {EventId} a {Destinatario}; queda pendiente de reintento.",
                eventId,
                notificacion.Destinatario);
        }
    }

    /// <summary>
    /// Reintenta las pendientes cuyo turno llego: las entregadas pasan a ENVIADA y las
    /// que fallan agendan su siguiente intento o quedan FALLIDA. Devuelve cuantas trato.
    /// </summary>
    public async Task<int> ReintentarPendientesAsync(CancellationToken cancelacion)
    {
        var pendientes = await _pendientes.ListarParaReintentarAsync(_reloj(), LoteReintentos, cancelacion);

        foreach (var pendiente in pendientes)
        {
            try
            {
                await _mensajeria.EnviarEmailAsync(pendiente.Destinatario, pendiente.Asunto, pendiente.Cuerpo, cancelacion);
                pendiente.MarcarEnviada(_reloj());
                _logger.LogInformation(
                    "Notificacion pendiente {Id} (evento {EventId}) entregada en el reintento {Intento}.",
                    pendiente.Id, pendiente.EventId, pendiente.Intentos);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
            {
                pendiente.RegistrarFallo(ex.Message, _reloj());
                if (pendiente.Estado == EstadoNotificacionPendiente.FALLIDA)
                {
                    _logger.LogError(
                        "Notificacion pendiente {Id} (evento {EventId}) FALLIDA tras {Intentos} reintentos.",
                        pendiente.Id, pendiente.EventId, pendiente.Intentos);
                }
            }
        }

        if (pendientes.Count > 0)
        {
            await _pendientes.GuardarCambiosAsync(cancelacion);
        }

        return pendientes.Count;
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
