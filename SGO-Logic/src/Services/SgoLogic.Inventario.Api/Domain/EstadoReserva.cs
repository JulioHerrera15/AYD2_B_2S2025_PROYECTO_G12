namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Ciclo de vida de una reserva de inventario. Local al contexto Inventario.</summary>
public enum EstadoReserva
{
    /// <summary>Reserva vigente, con TTL sin vencer.</summary>
    ACTIVA,

    /// <summary>El TTL vencio y las unidades volvieron al disponible (HU-002).</summary>
    EXPIRADA,

    /// <summary>Liberada por compensacion de la saga antes de vencer (HU-003).</summary>
    LIBERADA
}
