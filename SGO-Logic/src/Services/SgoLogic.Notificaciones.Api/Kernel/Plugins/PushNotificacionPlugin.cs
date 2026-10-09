using System.Diagnostics;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Kernel.Plugins;

public sealed class PushNotificacionPlugin : INotificacionPlugin
{
    private readonly IMockMensajeriaClient _mensajeria;
    private readonly ILogger<PushNotificacionPlugin> _logger;
    private bool _habilitado = true;

    public PushNotificacionPlugin(IMockMensajeriaClient mensajeria, ILogger<PushNotificacionPlugin> logger)
    {
        _mensajeria = mensajeria;
        _logger = logger;
    }

    public PluginMetadata Metadata { get; } = new(
        Id: "plugin.push",
        Nombre: "Adaptador de Notificaciones Push Web/Móvil",
        Canal: "PUSH",
        Version: "1.0.0",
        Prioridad: 70,
        HabilitadoPorDefecto: true
    );

    public bool EstaHabilitado => _habilitado;

    public void ConfigurarHabilitado(bool habilitado) => _habilitado = habilitado;

    public bool SoportaCanal(string canal) =>
        string.Equals(canal, "PUSH", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultadoDespachoPlugin> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await _mensajeria.EnviarPushAsync(solicitud.Destinatario, solicitud.Asunto, solicitud.Cuerpo, cancelacion);
            cronometro.Stop();

            return new ResultadoDespachoPlugin(
                Exitoso: true,
                Canal: "PUSH",
                PluginId: Metadata.Id,
                MensajeIdExterno: Guid.NewGuid().ToString("N")[..8],
                Error: null,
                Duracion: cronometro.Elapsed
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
        {
            cronometro.Stop();
            _logger.LogWarning(ex, "Fallo en plugin PUSH hacia {Destinatario}: {Mensaje}", solicitud.Destinatario, ex.Message);

            return new ResultadoDespachoPlugin(
                Exitoso: false,
                Canal: "PUSH",
                PluginId: Metadata.Id,
                MensajeIdExterno: null,
                Error: ex.Message,
                Duracion: cronometro.Elapsed
            );
        }
    }
}
