using Prometheus;

namespace SgoLogic.BuildingBlocks.Metricas;

/// <summary>
/// Metricas del outbox y del consumo de mensajes, expuestas en /metrics (formato
/// Prometheus) por UseSgoDefaults(). Un solo registro estatico por proceso: cada
/// servicio expone las suyas propias, no hay agregacion entre servicios aqui.
/// </summary>
public static class MetricasSgo
{
    public static readonly Gauge OutboxPendientes = Prometheus.Metrics.CreateGauge(
        "outbox_pendientes",
        "Filas de la tabla outbox de este servicio en estado Pendiente.");

    public static readonly Counter OutboxPublicadosTotal = Prometheus.Metrics.CreateCounter(
        "outbox_publicados_total",
        "Mensajes de outbox publicados y confirmados por el broker.");

    public static readonly Counter OutboxFallidosTotal = Prometheus.Metrics.CreateCounter(
        "outbox_fallidos_total",
        "Mensajes de outbox que agotaron el maximo de reintentos y se enviaron a sgo.eventos.dlx.");

    public static readonly Histogram OutboxLatenciaPublicacionSegundos = Prometheus.Metrics.CreateHistogram(
        "outbox_latencia_publicacion_segundos",
        "Segundos entre que la fila de outbox se crea y se publica confirmada.");

    public static readonly Counter MensajesConsumidosTotal = Prometheus.Metrics.CreateCounter(
        "mensajes_consumidos_total",
        "Mensajes de eventos procesados exitosamente por un EventConsumerBase.");

    public static readonly Counter MensajesDuplicadosTotal = Prometheus.Metrics.CreateCounter(
        "mensajes_duplicados_total",
        "Mensajes descartados por EventConsumerBase por tener un eventId ya procesado.");
}