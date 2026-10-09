using System.Diagnostics;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Kernel.Plugins;

public sealed class SmsNotificacionPlugin : INotificacionPlugin
{
    private readonly IMockMensajeriaClient _mensajeria;
    private readonly ILogger<SmsNotificacionPlugin> _logger;
    private bool _habilitado = true;

    public SmsNotificacionPlugin(IMockMensajeriaClient mensajeria, ILogger<SmsNotificacionPlugin> logger)
    {
        _mensajeria = mensajeria;
        _logger = logger;
    }

    public PluginMetadata Metadata { get; } = new(
        Id: "plugin.sms",
        Nombre: "Adaptador de Mensajería Celular (SMS)",
        Canal: "SMS",
        Version: "1.0.0",
        Prioridad: 80,
        HabilitadoPorDefecto: true
    );

    public bool EstaHabilitado => _habilitado;

    public void ConfigurarHabilitado(bool habilitado) => _habilitado = habilitado;

    public bool SoportaCanal(string canal) =>
        string.Equals(canal, "SMS", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultadoDespachoPlugin> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            var textoSms = $"[SGO] {solicitud.Asunto}: {solicitud.Cuerpo}";
            await _mensajeria.EnviarSmsAsync(solicitud.Destinatario, textoSms, cancelacion);
            cronometro.Stop();

            return new ResultadoDespachoPlugin(
                Exitoso: true,
                Canal: "SMS",
                PluginId: Metadata.Id,
                MensajeIdExterno: Guid.NewGuid().ToString("N")[..8],
                Error: null,
                Duracion: cronometro.Elapsed
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
        {
            cronometro.Stop();
            _logger.LogWarning(ex, "Fallo en plugin SMS hacia {Destinatario}: {Mensaje}", solicitud.Destinatario, ex.Message);

            return new ResultadoDespachoPlugin(
                Exitoso: false,
                Canal: "SMS",
                PluginId: Metadata.Id,
                MensajeIdExterno: null,
                Error: ex.Message,
                Duracion: cronometro.Elapsed
            );
        }
    }
}
