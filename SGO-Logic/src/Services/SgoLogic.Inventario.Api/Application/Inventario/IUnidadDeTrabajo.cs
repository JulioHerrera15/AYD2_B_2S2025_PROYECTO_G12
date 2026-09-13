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

    /// <summary>
    /// Descarta los cambios pendientes que nunca llegaron a confirmarse, dejando el
    /// contexto limpio para la siguiente operacion. Lo usa el barrido de expiracion
    /// (HU-002), que procesa varias reservas en un mismo alcance: si una falla, sus
    /// cambios (existencias, reserva y filas de outbox) no deben arrastrarse al
    /// intento de la siguiente. La reserva que fallo sigue ACTIVA y vencida, asi que
    /// el proximo ciclo la vuelve a tomar.
    /// </summary>
    void DescartarCambios();
}
