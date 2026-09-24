using Prometheus;

namespace SgoLogic.Auditoria.Worker.Application;

/// <summary>
/// Metricas propias de Auditoria, expuestas en /metrics junto con las
/// transversales de MetricasSgo (mensajes_consumidos_total,
/// mensajes_duplicados_total, etc.), que este servicio tambien emite porque
/// hereda de EventConsumerBase.
///
/// auditoria_eventos_descartados_total NO duplica mensajes_duplicados_total: ese
/// transversal ya cuenta, en este mismo proceso, los duplicados que
/// EventConsumerBase descarta antes de llamar a ManejarAsync (por eventId +
/// nombre de servicio ya en mensajes_procesados) — como Auditoria es el unico
/// consumidor de este proceso, ese contador YA es, de hecho, el contador de
/// duplicados descartados de Auditoria (ver AUDITORIA.md). Este contador propio
/// cubre ademas el chequeo defensivo por unicidad de EventId directamente sobre
/// eventos_auditoria que hace RegistradorEventoAuditoria (defensa en profundidad,
/// casi nunca se dispara en el flujo normal).
/// </summary>
public static class MetricasAuditoria
{
    public static readonly Counter EventosRegistradosTotal = Metrics.CreateCounter(
        "auditoria_eventos_registrados_total",
        "Eventos insertados en eventos_auditoria, por source de origen.",
        new CounterConfiguration { LabelNames = new[] { "source" } });

    public static readonly Histogram ConsultaEvidenciaSegundos = Metrics.CreateHistogram(
        "auditoria_consulta_evidencia_segundos",
        "Latencia de GET /auditoria/transacciones/{transactionId} (EAC-03: recuperar toda la evidencia de una transaccion en menos de 1 segundo).");

    public static readonly Counter EventosDescartadosTotal = Metrics.CreateCounter(
        "auditoria_eventos_descartados_total",
        "Eventos descartados por el chequeo defensivo de EventId ya existente en eventos_auditoria.");
}