using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 2 del contrato ordenes-eventos-v1. Se emite en toda transicion posterior
/// a la creacion, sin excepcion. Es la fuente autoritativa del estado (seccion 6
/// del contrato). eventType en PascalCase; la forma kebab-case
/// (orden.estado-cambiado) es la routing key, no el eventType.
/// </summary>
public static class OrdenEstadoCambiado
{
    public const string EventType = "OrdenEstadoCambiado";
    public const string RoutingKey = "ordenes.orden.estado-cambiado";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento OrdenEstadoCambiado (seccion 7.2 del contrato).</summary>
public sealed record OrdenEstadoCambiadoData(
    string OrdenId,
    string NumeroOrden,
    string ClienteId,
    EstadoOrden EstadoAnterior,
    EstadoOrden EstadoNuevo,
    string? Motivo,
    PasoSaga? PasoSaga,
    bool EsCompensacion,
    DateTime CambiadoEn);
