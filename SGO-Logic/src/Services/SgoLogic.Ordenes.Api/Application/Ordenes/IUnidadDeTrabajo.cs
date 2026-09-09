namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Confirma en una sola transaccion de base de datos todos los cambios acumulados
/// en los repositorios del contexto. La implementacion traduce el conflicto de
/// bloqueo optimista de EF Core a <see cref="ExcepcionConflictoConcurrencia" />.
/// </summary>
public interface IUnidadDeTrabajo
{
    Task GuardarCambiosAsync(CancellationToken cancelacion);
}
