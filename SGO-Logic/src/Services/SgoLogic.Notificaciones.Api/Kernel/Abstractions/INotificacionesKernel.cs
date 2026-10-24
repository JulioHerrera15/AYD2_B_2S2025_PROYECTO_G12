namespace SgoLogic.Notificaciones.Api.Kernel.Abstractions;

/// <summary>
/// Núcleo del estilo arquitectónico Microkernel.
/// Orquesta el despacho de notificaciones hacia los plugins registrados,
/// garantiza el aislamiento de fallas entre canales y coordina el almacenamiento
/// de notificaciones pendientes para reintentos con backoff.
/// </summary>
public interface INotificacionesKernel
{
    IPluginRegistry Plugins { get; }

    Task<ResultadoKernel> DespacharAsync(SolicitudNotificacion solicitud, CancellationToken cancelacion = default);

    Task<ResultadoDespachoPlugin> DespacharReintentoAsync(string canal, string destinatario, string asunto, string cuerpo, CancellationToken cancelacion = default);
}
//prueba