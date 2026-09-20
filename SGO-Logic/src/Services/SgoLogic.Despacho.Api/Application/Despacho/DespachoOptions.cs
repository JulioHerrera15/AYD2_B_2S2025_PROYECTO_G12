namespace SgoLogic.Despacho.Api.Application.Despacho;

/// <summary>Parametros del contexto Despacho.</summary>
public sealed class DespachoOptions
{
    public const string Seccion = "Despacho";

    /// <summary>
    /// Cuanto se espera a LogiExpress antes de dar la guia por pendiente. Corto a
    /// proposito: el criterio de aceptacion quiere que el despacho fisico arranque
    /// igual, asi que no tiene sentido bloquear el consumidor esperando.
    /// </summary>
    public int LogiExpressTimeoutMs { get; set; } = 3000;

    /// <summary>Timeout de la llamada interna a Inventario.</summary>
    public int InventarioTimeoutMs { get; set; } = 3000;
}
