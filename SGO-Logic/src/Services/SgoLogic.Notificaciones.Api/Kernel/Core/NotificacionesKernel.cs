using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Domain;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Kernel.Core;

/// <summary>
/// Implementación del Microkernel de Notificaciones.
/// Coordina la ejecución aislada de plugins, el manejo de fallas por canal
/// y el registro de notificaciones pendientes en base de datos.
/// </summary>
public sealed class NotificacionesKernel : INotificacionesKernel
{
    private readonly IPluginRegistry _plugins;
    private readonly IRepositorioNotificacionesPendientes _pendientes;
    private readonly ILogger<NotificacionesKernel> _logger;
    private readonly Func<DateTime> _reloj;

    public NotificacionesKernel(
        IPluginRegistry plugins,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<NotificacionesKernel> logger)
        : this(plugins, pendientes, logger, () => DateTime.UtcNow)
    {
    }

    public NotificacionesKernel(
        IPluginRegistry plugins,
        IRepositorioNotificacionesPendientes pendientes,
        ILogger<NotificacionesKernel> logger,
        Func<DateTime> reloj)
    {
        _plugins = plugins;
        _pendientes = pendientes;
        _logger = logger;
        _reloj = reloj;
    }

    public IPluginRegistry Plugins => _plugins;

    public async Task<ResultadoKernel> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var pluginsDestino = _plugins.ObtenerPorCanal(solicitud.Canal);

        if (pluginsDestino.Count == 0)
        {
            _logger.LogWarning(
                "Microkernel: No existen plugins habilitados para el canal '{Canal}' (Evento: {EventId})",
                solicitud.Canal, solicitud.EventId);

            return new ResultadoKernel(
                EventId: solicitud.EventId,
                TodoExitoso: false,
                ResultadosPorPlugin: Array.Empty<ResultadoDespachoPlugin>(),
                PluginsEjecutados: 0,
                FallosRegistrados: 1
            );
        }

        var resultados = new List<ResultadoDespachoPlugin>();
        var fallos = 0;

        foreach (var plugin in pluginsDestino)
        {
            try
            {
                var resultado = await plugin.DespacharAsync(solicitud, cancelacion);
                resultados.Add(resultado);

                if (!resultado.Exitoso)
                {
                    fallos++;
                    RegistrarPendiente(solicitud, plugin.Metadata.Canal, resultado.Error);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
            {
                fallos++;
                _logger.LogError(ex, "Microkernel: Excepción inesperada en plugin {PluginId}: {Mensaje}",
                    plugin.Metadata.Id, ex.Message);

                resultados.Add(new ResultadoDespachoPlugin(
                    Exitoso: false,
                    Canal: plugin.Metadata.Canal,
                    PluginId: plugin.Metadata.Id,
                    MensajeIdExterno: null,
                    Error: ex.Message,
                    Duracion: TimeSpan.Zero
                ));

                RegistrarPendiente(solicitud, plugin.Metadata.Canal, ex.Message);
            }
        }

        return new ResultadoKernel(
            EventId: solicitud.EventId,
            TodoExitoso: fallos == 0,
            ResultadosPorPlugin: resultados,
            PluginsEjecutados: pluginsDestino.Count,
            FallosRegistrados: fallos
        );
    }

    public async Task<ResultadoDespachoPlugin> DespacharReintentoAsync(
        string canal,
        string destinatario,
        string asunto,
        string cuerpo,
        CancellationToken cancelacion = default)
    {
        var plugins = _plugins.ObtenerPorCanal(canal);
        if (plugins.Count == 0)
        {
            return new ResultadoDespachoPlugin(
                Exitoso: false,
                Canal: canal,
                PluginId: "ninguno",
                MensajeIdExterno: null,
                Error: $"No hay plugins activos para el canal {canal}",
                Duracion: TimeSpan.Zero
            );
        }

        var plugin = plugins[0]; // Selecciona el de mayor prioridad
        var solicitud = new SolicitudNotificacion(
            EventId: Guid.NewGuid().ToString("N"),
            Canal: canal,
            Destinatario: destinatario,
            Asunto: asunto,
            Cuerpo: cuerpo
        );

        return await plugin.DespacharAsync(solicitud, cancelacion);
    }

    private void RegistrarPendiente(SolicitudNotificacion solicitud, string canal, string? error)
    {
        try
        {
            var pendiente = new NotificacionPendiente(
                eventId: solicitud.EventId,
                canal: canal,
                destinatario: solicitud.Destinatario,
                asunto: solicitud.Asunto,
                cuerpo: solicitud.Cuerpo,
                error: error,
                ahora: _reloj()
            );

            _pendientes.Agregar(pendiente);

            _logger.LogWarning(
                "Microkernel: Notificación pendiente encolada para canal '{Canal}' hacia {Destinatario} (Evento: {EventId})",
                canal, solicitud.Destinatario, solicitud.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Microkernel: Error al registrar notificación pendiente para evento {EventId}", solicitud.EventId);
        }
    }
}
