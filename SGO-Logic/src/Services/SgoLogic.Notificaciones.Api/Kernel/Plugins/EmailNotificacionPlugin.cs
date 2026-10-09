using System.Diagnostics;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Kernel.Plugins;

public sealed class EmailNotificacionPlugin : INotificacionPlugin
{
    private readonly IMockMensajeriaClient _mensajeria;
    private readonly ILogger<EmailNotificacionPlugin> _logger;
    private bool _habilitado = true;

    public EmailNotificacionPlugin(IMockMensajeriaClient mensajeria, ILogger<EmailNotificacionPlugin> logger)
    {
        _mensajeria = mensajeria;
        _logger = logger;
    }

    public PluginMetadata Metadata { get; } = new(
        Id: "plugin.email",
        Nombre: "Adaptador de Correo Electrónico (Email)",
        Canal: "EMAIL",
        Version: "1.0.0",
        Prioridad: 100,
        HabilitadoPorDefecto: true
    );

    public bool EstaHabilitado => _habilitado;

    public void ConfigurarHabilitado(bool habilitado) => _habilitado = habilitado;

    public bool SoportaCanal(string canal) =>
        string.Equals(canal, "EMAIL", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultadoDespachoPlugin> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await _mensajeria.EnviarEmailAsync(solicitud.Destinatario, solicitud.Asunto, solicitud.Cuerpo, cancelacion);
            cronometro.Stop();

            return new ResultadoDespachoPlugin(
                Exitoso: true,
                Canal: "EMAIL",
                PluginId: Metadata.Id,
                MensajeIdExterno: Guid.NewGuid().ToString("N")[..8],
                Error: null,
                Duracion: cronometro.Elapsed
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
        {
            cronometro.Stop();
            _logger.LogWarning(ex, "Fallo en plugin EMAIL hacia {Destinatario}: {Mensaje}", solicitud.Destinatario, ex.Message);

            return new ResultadoDespachoPlugin(
                Exitoso: false,
                Canal: "EMAIL",
                PluginId: Metadata.Id,
                MensajeIdExterno: null,
                Error: ex.Message,
                Duracion: cronometro.Elapsed
            );
        }
    }
}
