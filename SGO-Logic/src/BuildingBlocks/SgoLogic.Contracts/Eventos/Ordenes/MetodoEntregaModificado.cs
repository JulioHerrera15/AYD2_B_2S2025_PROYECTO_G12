using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Contracts.Eventos.Ordenes;

/// <summary>
/// Evento 10 del contrato ordenes-eventos-v1. La orden es un agregado de larga vida
/// y mutable: el metodo de entrega puede cambiar despues de confirmada (EV-31,
/// CUN-01.5). Despacho lo usa para reasignar guia u origen (HU-011).
/// </summary>
public static class MetodoEntregaModificado
{
    public const string EventType = "MetodoEntregaModificado";
    public const string RoutingKey = "ordenes.orden.metodo-entrega-modificado";
    public const string AggregateType = "Orden";
}

/// <summary>Contenido de data del evento MetodoEntregaModificado (seccion 7.10 del contrato).</summary>
public sealed record MetodoEntregaModificadoData(
    string OrdenId,
    MetodoEntrega MetodoEntregaAnterior,
    MetodoEntrega MetodoEntregaNuevo,
    string? UbicacionRecogidaId,
    DireccionEntregaData? DireccionEntrega,
    SolicitanteCambio SolicitadoPor,
    DateTime ModificadoEn);
