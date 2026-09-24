namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Seccion "PayKlik" de la configuracion. Agrupa tres cosas del adaptador hacia el
/// proveedor: la invocacion REST (URL base y timeout), el disyuntor / circuit
/// breaker que envuelve esa llamada HTTP (ADR-009) y los reintentos con backoff
/// que aplica el servicio de cobro alrededor de "registrar intento + invocar".
/// </summary>
public sealed class PayKlikOptions
{
    public const string Seccion = "PayKlik";

    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutMs { get; set; } = 5000;

    // --- Circuit breaker (ADR-009). Cuentan como fallo: timeout, error de
    // conexion y respuestas 5xx. NO cuenta un rechazo de negocio. ---

    /// <summary>Ventana de observacion del ratio de fallo, en segundos.</summary>
    public int VentanaSegundos { get; set; } = 30;

    /// <summary>Llamadas minimas dentro de la ventana antes de que el ratio pueda abrir el circuito.</summary>
    public int MinimoLlamadas { get; set; } = 3;

    /// <summary>Proporcion de fallo (0..1) que abre el circuito una vez alcanzado el minimo de llamadas.</summary>
    public double RatioFallo { get; set; } = 0.5;

    /// <summary>Tiempo que el circuito permanece abierto antes de pasar a semiabierto, en segundos.</summary>
    public int AperturaSegundos { get; set; } = 15;

    // --- Reintentos con backoff. Se reintenta solo ante timeout, error de
    // conexion o 5xx; nunca ante rechazo de negocio ni con el circuito abierto. ---

    /// <summary>Reintentos adicionales tras la primera llamada. 1 = hasta dos llamadas en total.</summary>
    public int MaxReintentos { get; set; } = 1;

    /// <summary>Base del backoff exponencial con jitter, en milisegundos.</summary>
    public int BackoffBaseMs { get; set; } = 1000;
}
