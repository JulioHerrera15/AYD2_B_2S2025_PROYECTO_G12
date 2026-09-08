namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>Configuracion del outbox, seccion "Outbox" de appsettings / variables de entorno.</summary>
public sealed class OutboxOptions
{
    public const string Seccion = "Outbox";

    /// <summary>Contexto emisor en minuscula (va en el campo "source" del envelope), ej. "ordenes".</summary>
    public string Source { get; set; } = string.Empty;

    public int PollingIntervalMs { get; set; } = 2000;

    public int BatchSize { get; set; } = 20;

    public int MaxReintentos { get; set; } = 5;
}