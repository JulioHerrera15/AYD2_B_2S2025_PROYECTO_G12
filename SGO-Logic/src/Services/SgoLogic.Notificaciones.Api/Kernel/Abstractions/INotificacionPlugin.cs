namespace SgoLogic.Notificaciones.Api.Kernel.Abstractions;

/// <summary>
/// Contrato base de extensibilidad para plugins en el estilo Microkernel.
/// Cada canal de notificación (Email, SMS, Push, Webhook) se implementa como un plugin independiente.
/// </summary>
public interface INotificacionPlugin
{
    PluginMetadata Metadata { get; }

    bool EstaHabilitado { get; }

    void ConfigurarHabilitado(bool habilitado);

    bool SoportaCanal(string canal);

    Task<ResultadoDespachoPlugin> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default);
}
