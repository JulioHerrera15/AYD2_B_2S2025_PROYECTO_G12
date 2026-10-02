using Prometheus;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Metricas propias de Credito, expuestas en /metrics junto con las transversales
/// de MetricasSgo (outbox y consumo de mensajes).
/// </summary>
public static class MetricasCredito
{
    public static readonly Counter ConsultasElegibilidadTotal = Metrics.CreateCounter(
        "credito_consultas_elegibilidad_total",
        "Consultas de GET /api/credito/elegibilidad, por estado de la oferta devuelta.",
        new CounterConfiguration { LabelNames = new[] { "estado" } });

    public static readonly Counter PerfilesRecalculadosTotal = Metrics.CreateCounter(
        "credito_perfiles_recalculados_total",
        "Perfiles crediticios recalculados a partir de un historial nuevo (cambien o no de resultado).");
}
