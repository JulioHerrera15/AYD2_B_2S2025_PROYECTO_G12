namespace SgoLogic.Contracts.Eventos.Pagos;

/// <summary>
/// Estado del agregado Pago del contexto Pagos (contrato pagos-eventos-v1). Lista
/// cerrada. Se ubica aca, junto a los eventos, y no en
/// SgoLogic.Contracts.Enumeraciones, porque ese directorio es del dueno de la base
/// tecnica; este enum lo aporta el contrato de Pagos.
/// </summary>
public enum EstadoPago
{
    INICIADO,
    APLICADO,
    PENDIENTE,
    RECHAZADO
}
