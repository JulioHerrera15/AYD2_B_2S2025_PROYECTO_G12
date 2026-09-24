namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Razon por la que cambio la cantidad fisica de una existencia.</summary>
public enum MotivoMovimiento
{
    /// <summary>Alta de la existencia: es el primer asiento del ledger de ese SKU + ubicacion.</summary>
    CARGA_INICIAL,

    /// <summary>Correccion de la cantidad fisica sobre una existencia ya dada de alta (recuento, merma).</summary>
    AJUSTE_MANUAL,

    /// <summary>Devolucion que repone unidades en la ubicacion (HU-014).</summary>
    REINGRESO_DEVOLUCION
}
