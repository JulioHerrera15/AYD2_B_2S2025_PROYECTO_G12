using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;

/// <summary>
/// Datos del hecho "la orden cambio de estado". Se transporta dentro del envelope
/// <see cref="IntegrationEvent{TData}" /> de SgoLogic.Contracts. El payload
/// serializado es lo que se guarda en la fila de outbox.
/// </summary>
public sealed record OrdenEstadoCambiadoData(
    string OrdenId,
    string NumeroOrden,
    EstadoOrden EstadoAnterior,
    EstadoOrden EstadoNuevo,
    int Secuencia,
    PasoSaga? PasoSaga,
    string? Motivo,
    DateTime OcurridoEn);

/// <summary>
/// Fabrica del evento de integracion que se emite al mover el estado de una orden.
/// El tipo del dato se mantiene local al contexto Ordenes; solo se reutiliza el
/// envelope compartido.
/// </summary>
public static class OrdenEstadoCambiadoEvento
{
    public const string TipoEvento = "orden.estado-cambiado";
    public const string TipoAgregado = "Orden";
    public const string Fuente = "sgo-logic-ordenes-api";

    public static IntegrationEvent<OrdenEstadoCambiadoData> Crear(
        string transactionId,
        string aggregateId,
        int sequence,
        OrdenEstadoCambiadoData data,
        string? causationId = null)
    {
        return new IntegrationEvent<OrdenEstadoCambiadoData>
        {
            EventType = TipoEvento,
            TransactionId = transactionId,
            Sequence = sequence,
            CausationId = causationId,
            AggregateType = TipoAgregado,
            AggregateId = aggregateId,
            Source = Fuente,
            Data = data
        };
    }
}
