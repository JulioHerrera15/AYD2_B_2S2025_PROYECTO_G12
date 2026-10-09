namespace SgoLogic.Notificaciones.Api.Kernel.Abstractions;

public sealed record PluginMetadata(
    string Id,
    string Nombre,
    string Canal,
    string Version,
    int Prioridad,
    bool HabilitadoPorDefecto
);

public sealed record SolicitudNotificacion(
    string EventId,
    string Canal,
    string Destinatario,
    string Asunto,
    string Cuerpo,
    IReadOnlyDictionary<string, string>? Metadatos = null
);

public sealed record ResultadoDespachoPlugin(
    bool Exitoso,
    string Canal,
    string PluginId,
    string? MensajeIdExterno,
    string? Error,
    TimeSpan Duracion
);

public sealed record ResultadoKernel(
    string EventId,
    bool TodoExitoso,
    IReadOnlyList<ResultadoDespachoPlugin> ResultadosPorPlugin,
    int PluginsEjecutados,
    int FallosRegistrados
);
