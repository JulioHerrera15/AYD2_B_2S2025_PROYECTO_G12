using System.Collections.Concurrent;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Kernel.Core;

/// <summary>
/// Implementación concurrente y ordenada por prioridad del registro de plugins.
/// </summary>
public sealed class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, INotificacionPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public void Registrar(INotificacionPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _plugins[plugin.Metadata.Id] = plugin;
    }

    public IReadOnlyList<INotificacionPlugin> ObtenerTodos() =>
        _plugins.Values
            .OrderByDescending(p => p.Metadata.Prioridad)
            .ToList();

    public IReadOnlyList<INotificacionPlugin> ObtenerPorCanal(string canal)
    {
        if (string.IsNullOrWhiteSpace(canal) || canal == "*")
        {
            return _plugins.Values
                .Where(p => p.EstaHabilitado)
                .OrderByDescending(p => p.Metadata.Prioridad)
                .ToList();
        }

        return _plugins.Values
            .Where(p => p.EstaHabilitado && p.SoportaCanal(canal))
            .OrderByDescending(p => p.Metadata.Prioridad)
            .ToList();
    }

    public INotificacionPlugin? ObtenerPorId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _plugins.TryGetValue(id, out var plugin) ? plugin : null;
    }
}
