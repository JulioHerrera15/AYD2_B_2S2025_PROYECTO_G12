namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Ciclo de vida de una reserva de inventario. Local al contexto Inventario.</summary>
public enum EstadoReserva
{
    /// <summary>Reserva vigente, con TTL sin vencer.</summary>
    ACTIVA,

    /// <summary>
    /// La orden fue pagada y confirmada: las unidades siguen retenidas hasta que el
    /// operador envia el pedido a ruta. No expira por TTL.
    /// </summary>
    CONFIRMADA,

    /// <summary>El TTL vencio y las unidades volvieron al disponible (HU-002).</summary>
    EXPIRADA,

    /// <summary>Liberada por compensacion de la saga antes de vencer (HU-003).</summary>
    LIBERADA,

    /// <summary>La orden fue entregada y las unidades salieron fisicamente del almacen.</summary>
    ENTREGADA
}
