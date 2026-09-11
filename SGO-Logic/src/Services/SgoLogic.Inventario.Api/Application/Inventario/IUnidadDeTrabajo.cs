namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Confirma en una sola transaccion de base de datos todos los cambios acumulados
/// en los repositorios del contexto (existencias, reserva y filas del outbox). La
/// implementacion traduce el conflicto de bloqueo optimista de EF Core a
/// <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public interface IUnidadDeTrabajo
{
    Task GuardarCambiosAsync(CancellationToken cancelacion);
}
