using System.Diagnostics;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Kernel.Plugins;

public sealed class WebhookNotificacionPlugin : INotificacionPlugin
{
    private readonly IMockMensajeriaClient _mensajeria;
    private readonly ILogger<WebhookNotificacionPlugin> _logger;
    private bool _habilitado = true;

    public WebhookNotificacionPlugin(IMockMensajeriaClient mensajeria, ILogger<WebhookNotificacionPlugin> logger)
    {
        _mensajeria = mensajeria;
        _logger = logger;
    }

    public PluginMetadata Metadata { get; } = new(
        Id: "plugin.webhook",
        Nombre: "Adaptador de Webhooks HTTP Salientes",
        Canal: "WEBHOOK",
        Version: "1.0.0",
        Prioridad: 50,
        HabilitadoPorDefecto: true
    );

    public bool EstaHabilitado => _habilitado;

    public void ConfigurarHabilitado(bool habilitado) => _habilitado = habilitado;

    public bool SoportaCanal(string canal) =>
        string.Equals(canal, "WEBHOOK", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultadoDespachoPlugin> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await _mensajeria.EnviarWebhookAsync(solicitud.Destinatario, solicitud.Asunto, solicitud.Cuerpo, cancelacion);
            cronometro.Stop();

            return new ResultadoDespachoPlugin(
                Exitoso: true,
                Canal: "WEBHOOK",
                PluginId: Metadata.Id,
                MensajeIdExterno: Guid.NewGuid().ToString("N")[..8],
                Error: null,
                Duracion: cronometro.Elapsed
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
        {
            cronometro.Stop();
            _logger.LogWarning(ex, "Fallo en plugin WEBHOOK hacia {Destinatario}: {Mensaje}", solicitud.Destinatario, ex.Message);

            return new ResultadoDespachoPlugin(
                Exitoso: false,
                Canal: "WEBHOOK",
                PluginId: Metadata.Id,
                MensajeIdExterno: null,
                Error: ex.Message,
                Duracion: cronometro.Elapsed
            );
        }
    }
}
