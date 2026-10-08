namespace SgoLogic.Contracts.Enumeraciones;

/// <summary>
/// Estados de la maquina de la saga de una orden. Lista cerrada segun el contrato
/// ordenes-eventos-v1. Cualquier valor nuevo exige una version del contrato.
/// </summary>
public enum EstadoOrden
{
    CREADA,
    RESERVA_SOLICITADA,
    INVENTARIO_RESERVADO,
    COBRO_SOLICITADO,
    PAGO_PENDIENTE,
    PAGO_APLICADO,
    CONFIRMADA_PENDIENTE_FISCAL,
    CONFIRMADA,
    DESPACHO_SOLICITADO,
    EN_PREPARACION,
    EN_TRANSITO,
    LISTA_PARA_RECOGER,
    ENTREGADA,
    DEVOLUCION_EN_PROCESO,
    DEVUELTA,
    CANCELADA_RESERVA_VENCIDA,
    CANCELADA,
    RECHAZADA,
    EVALUACION_CREDITO_SOLICITADA,
    CREDITO_APROBADO
}
