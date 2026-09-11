namespace SgoLogic.Contracts.Enumeraciones;

/// <summary>
/// Motivo por el que Inventario no pudo reservar lo solicitado (contrato
/// inventario-eventos-v1, evento ReservaInventarioRechazada). Lista cerrada.
/// </summary>
public enum MotivoRechazoReserva
{
    /// <summary>Alguna linea pide mas unidades de las disponibles en toda ubicacion elegible.</summary>
    STOCK_INSUFICIENTE,

    /// <summary>Ninguna ubicacion tiene existencia registrada para todos los SKU solicitados.</summary>
    SIN_UBICACION_ELEGIBLE
}
