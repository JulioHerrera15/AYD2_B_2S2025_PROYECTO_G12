namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Sentido de un movimiento fisico de existencias. Local al contexto Inventario.
/// No cubre reservas: esas son compromisos logicos sobre la cantidad disponible y
/// no alteran la cantidad fisica.
/// </summary>
public enum TipoMovimiento
{
    /// <summary>Ingresaron unidades a la ubicacion (carga, recuento al alza, reingreso).</summary>
    ENTRADA,

    /// <summary>Salieron unidades de la ubicacion (despacho, recuento a la baja, merma).</summary>
    SALIDA
}
