using Microsoft.AspNetCore.Mvc;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;

namespace SgoLogic.Notificaciones.Api.Controllers;

public sealed record DespachoManualRequest(
    string Destinatario,
    string Asunto,
    string Cuerpo,
    string? Canal = "EMAIL"
);

/// <summary>
/// Controlador de administración y observabilidad del Microkernel de Notificaciones.
/// Permite auditar plugins instalados, cambiar su estado en tiempo de ejecución
/// y realizar pruebas de despacho multicanal.
/// </summary>
[ApiController]
[Route("api/kernel")]
public sealed class KernelPluginsController : ControllerBase
{
    private readonly INotificacionesKernel _kernel;
    private readonly ILogger<KernelPluginsController> _logger;

    public KernelPluginsController(INotificacionesKernel kernel, ILogger<KernelPluginsController> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    /// <summary>Lista todos los plugins instalados en el Microkernel y su estado.</summary>
    [HttpGet("plugins")]
    public IActionResult ObtenerPlugins()
    {
        var plugins = _kernel.Plugins.ObtenerTodos().Select(p => new
        {
            p.Metadata.Id,
            p.Metadata.Nombre,
            p.Metadata.Canal,
            p.Metadata.Version,
            p.Metadata.Prioridad,
            Habilitado = p.EstaHabilitado
        });

        return Ok(new
        {
            Arquitectura = "Microkernel (Plugin-based Architecture)",
            TotalPlugins = plugins.Count(),
            Plugins = plugins
        });
    }

    /// <summary>Activa o desactiva dinámicamente un plugin por su Id.</summary>
    [HttpPost("plugins/{id}/toggle")]
    public IActionResult CambiarEstadoPlugin(string id, [FromQuery] bool habilitado)
    {
        var plugin = _kernel.Plugins.ObtenerPorId(id);
        if (plugin is null)
        {
            return NotFound(new { Error = $"No se encontró el plugin con id '{id}'" });
        }

        plugin.ConfigurarHabilitado(habilitado);
        _logger.LogInformation("Microkernel: Plugin {PluginId} configurado a Habilitado = {Estado}", id, habilitado);

        return Ok(new
        {
            plugin.Metadata.Id,
            plugin.Metadata.Canal,
            Habilitado = plugin.EstaHabilitado
        });
    }

    /// <summary>Despacha una notificación directa a través del Microkernel para pruebas o demos.</summary>
    [HttpPost("despachar")]
    public async Task<IActionResult> DespacharDirecto([FromBody] DespachoManualRequest request, CancellationToken ct)
    {
        var solicitud = new SolicitudNotificacion(
            EventId: Guid.NewGuid().ToString("N"),
            Canal: request.Canal ?? "EMAIL",
            Destinatario: request.Destinatario,
            Asunto: request.Asunto,
            Cuerpo: request.Cuerpo
        );

        var resultado = await _kernel.DespacharAsync(solicitud, ct);

        return Ok(resultado);
    }
}
