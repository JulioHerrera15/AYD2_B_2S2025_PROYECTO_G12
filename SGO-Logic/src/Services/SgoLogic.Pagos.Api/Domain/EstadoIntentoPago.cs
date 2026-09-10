namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Ciclo de vida de un <see cref="IntentoPago" />. Interno de Pagos: no viaja en
/// eventos. Se persiste como texto, nunca como entero.
/// </summary>
public enum EstadoIntentoPago
{
    /// <summary>Registrado antes de invocar al proveedor. Nunca se borra.</summary>
    INICIADO,

    /// <summary>El proveedor aprobo el cargo.</summary>
    EXITOSO,

    /// <summary>El proveedor no resolvio: timeout, 5xx o error de red.</summary>
    FALLIDO,

    /// <summary>El proveedor rechazo el cargo de forma explicita.</summary>
    RECHAZADO
}
