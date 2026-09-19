namespace SgoLogic.Despacho.Api.Application.Despacho;

/// <summary>Choque de bloqueo optimista traducido para el endpoint REST (409).</summary>
public sealed class ExcepcionConflictoConcurrencia : Exception
{
    public ExcepcionConflictoConcurrencia(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}

/// <summary>No hay despacho para esa orden.</summary>
public sealed class ExcepcionDespachoNoEncontrado : Exception
{
    public ExcepcionDespachoNoEncontrado(string ordenId)
        : base($"No hay despacho para la orden '{ordenId}'.")
    {
    }
}
