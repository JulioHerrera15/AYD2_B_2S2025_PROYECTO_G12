namespace SgoLogic.Notificaciones.Api.Kernel.Abstractions;

/// <summary>
/// Catálogo y registro en memoria de plugins cargados en el Microkernel.
/// Permite descubrir, habilitar y resolver plugins por canal o identificador.
/// </summary>
public interface IPluginRegistry
{
    void Registrar(INotificacionPlugin plugin);

    IReadOnlyList<INotificacionPlugin> ObtenerTodos();

    IReadOnlyList<INotificacionPlugin> ObtenerPorCanal(string canal);

    INotificacionPlugin? ObtenerPorId(string id);
}
