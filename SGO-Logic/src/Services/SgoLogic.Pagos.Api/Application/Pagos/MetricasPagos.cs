using Prometheus;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>Metricas propias de Pagos, expuestas en /metrics junto con las transversales.</summary>
public static class MetricasPagos
{
    public static readonly Counter ReembolsosTotal = Metrics.CreateCounter(
        "pagos_reembolsos_total",
        "Reembolsos por cancelacion de orden y de abonos a credito, por concepto y estado alcanzado (pendiente, aplicado, fallido).",
        new CounterConfiguration { LabelNames = new[] { "concepto", "estado" } });
}
