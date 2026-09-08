namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Se lanza cuando el bloqueo optimista de la orden (columna version, token de
/// concurrencia) detecta que otra operacion modifico la fila entre la lectura y la
/// escritura. El endpoint la traduce a 409 con un codigo distinguible del 409 por
/// transicion invalida.
/// </summary>
public sealed class ExcepcionConflictoConcurrencia : Exception
{
    public ExcepcionConflictoConcurrencia(string mensaje, Exception excepcionInterna)
        : base(mensaje, excepcionInterna)
    {
    }
}
