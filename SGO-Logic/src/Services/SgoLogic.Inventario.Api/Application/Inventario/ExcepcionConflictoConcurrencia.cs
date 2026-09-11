namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Se lanza cuando el bloqueo optimista de una existencia (columna <c>version</c>)
/// detecta que otra operacion modifico la fila entre la lectura y la escritura
/// (ADR-005). El endpoint la traduce a 409 con codigo <c>conflicto_concurrencia</c>;
/// el consumidor de eventos deja que suba para que el mensaje se reintente.
/// </summary>
public sealed class ExcepcionConflictoConcurrencia : Exception
{
    public ExcepcionConflictoConcurrencia(string mensaje, Exception excepcionInterna)
        : base(mensaje, excepcionInterna)
    {
    }
}
