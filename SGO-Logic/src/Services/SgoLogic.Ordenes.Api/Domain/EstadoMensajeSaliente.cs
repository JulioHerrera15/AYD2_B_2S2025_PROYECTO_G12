namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Ciclo de vida de una fila del outbox propio del contexto Ordenes. Se persiste
/// como texto, nunca como entero, para que la tabla sea legible y estable ante
/// reordenamientos del enum.
/// </summary>
public enum EstadoMensajeSaliente
{
    Pendiente,
    Publicado,
    Fallido
}
