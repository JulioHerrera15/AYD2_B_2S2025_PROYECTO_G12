namespace SgoLogic.Facturacion.Api.Application.Facturacion;

/// <summary>
/// Seccion "Sat" de la configuracion. Reune la invocacion REST al servicio de la
/// SAT (URL base y timeout), la serie autorizada con la que se numeran los
/// documentos, y los reintentos con backoff persistido que aplica Facturacion
/// (ADR-011, HU-005).
/// </summary>
public sealed class SatOptions
{
    public const string Seccion = "Sat";

    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutMs { get; set; } = 5000;

    /// <summary>Serie autorizada. Se asigna al documento una sola vez, al crearlo.</summary>
    public string Serie { get; set; } = "A";

    /// <summary>Intentos maximos de certificacion contra la SAT antes de pasar el documento a ALERTA.</summary>
    public int MaxIntentos { get; set; } = 4;

    /// <summary>Base del backoff exponencial con jitter, en milisegundos.</summary>
    public int BackoffBaseMs { get; set; } = 2000;

    /// <summary>Cada cuanto el worker de reintentos barre los documentos vencidos, en milisegundos.</summary>
    public int PollingIntervalMs { get; set; } = 5000;

    /// <summary>Cuantos documentos vencidos toma el worker por ciclo.</summary>
    public int LotePolling { get; set; } = 20;

    public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);

    public TimeSpan Polling => TimeSpan.FromMilliseconds(PollingIntervalMs);

    /// <summary>
    /// Margen con el que el worker empuja <c>proximo_intento_en</c> al reencolar, para
    /// no volver a tomar el documento mientras el envio esta en curso. Cubre el
    /// timeout del cliente mas un ciclo de polling.
    /// </summary>
    public TimeSpan MargenReencolado => TimeSpan.FromMilliseconds(TimeoutMs + PollingIntervalMs);
}
