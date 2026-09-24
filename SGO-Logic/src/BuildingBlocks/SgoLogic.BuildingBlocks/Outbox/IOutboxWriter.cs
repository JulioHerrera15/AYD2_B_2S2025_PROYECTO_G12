namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>
/// Encola un evento de integracion en la tabla outbox del DbContext del propio
/// servicio (registrado via AddOutbox&lt;TDbContext&gt;()). No llama a
/// SaveChangesAsync: eso lo hace quien llama, en el mismo SaveChangesAsync que
/// guarda el cambio de negocio, para que ambos queden en la misma transaccion.
/// transactionId y causationId salen de EventContext; el codigo de aplicacion
/// nunca los pasa a mano.
/// </summary>
public interface IOutboxWriter
{
    /// <param name="sequence">
    /// Secuencia monotonica del agregado (empieza en 1, la asigna el propio
    /// agregado en la misma operacion que produce este hecho). No la inventa el
    /// outbox: viene de la logica de negocio que ya la lleva.
    /// </param>
    void Encolar<TData>(
        string eventType,
        string aggregateType,
        string aggregateId,
        int sequence,
        string exchange,
        string routingKey,
        TData data)
        where TData : notnull;
}