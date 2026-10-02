namespace SgoLogic.Web.Services;

/// <summary>
/// Texto que se muestra para los estados de la ruta de credito. Los demas estados
/// se siguen mostrando como hasta ahora en cada vista.
/// </summary>
public static class EtiquetasEstadoOrden
{
    public const string EvaluacionCredito = "EVALUACION_CREDITO_SOLICITADA";
    public const string CreditoAprobado = "CREDITO_APROBADO";

    public static bool EsRutaCredito(string? estado) => estado is EvaluacionCredito or CreditoAprobado;

    /// <summary>"Confirmando crédito" y "Crédito aprobado"; cualquier otro estado se devuelve igual.</summary>
    public static string Etiqueta(string? estado) => estado switch
    {
        EvaluacionCredito => "Confirmando crédito",
        CreditoAprobado => "Crédito aprobado",
        _ => estado ?? string.Empty
    };
}
