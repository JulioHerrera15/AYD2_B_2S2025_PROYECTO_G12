using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Un solo lugar para pedir a Pagos el reembolso de un abono: registra la solicitud en el
/// abono (para no pedir dos veces el mismo dinero) y encola ReembolsoAbonoCreditoSolicitado
/// en el outbox de la misma unidad de trabajo. No guarda: lo hace quien llama, junto con
/// el cambio que origina el reembolso (anulacion, cobro tardio o devolucion).
/// </summary>
public static class SolicitudesReembolsoAbono
{
    public static void Solicitar(IOutboxWriter outbox, AbonoCredito abono, decimal monto, string motivo, DateTime ahora)
    {
        abono.RegistrarSolicitudReembolso(monto, ahora);

        outbox.Encolar(
            ReembolsoAbonoCreditoSolicitado.EventType,
            ReembolsoAbonoCreditoSolicitado.AggregateType,
            abono.Id.ToString("N"),
            abono.Version,
            TopologiaMensajeria.ExchangeEventos,
            ReembolsoAbonoCreditoSolicitado.RoutingKey,
            new ReembolsoAbonoCreditoSolicitadoData(
                Guid.NewGuid().ToString("N"),
                abono.Id.ToString("N"),
                abono.Referencia,
                abono.ClienteId,
                abono.Moneda,
                monto,
                motivo,
                ahora));

        MetricasCredito.ReembolsosSolicitadosTotal
            .WithLabels(motivo == MotivosReembolsoAbono.Devolucion ? "devolucion" : "cancelacion")
            .Inc();
    }

    /// <summary>
    /// Reembolso completo de lo que aun no se pidio de un abono en REEMBOLSO_PENDIENTE. El
    /// motivo sale de la cuenta: DEVOLUCION si se anulo por devolucion; si no, CANCELACION.
    /// No hace nada si ya no queda nada por pedir.
    /// </summary>
    public static bool SolicitarCompleto(IOutboxWriter outbox, AbonoCredito abono, CuentaPorCobrar? cuenta, DateTime ahora)
    {
        var monto = abono.PendientePorSolicitarReembolso;
        if (monto <= 0m)
        {
            return false;
        }

        var motivo = cuenta?.AnuladaPorDevolucion == true ? MotivosReembolsoAbono.Devolucion : MotivosReembolsoAbono.Cancelacion;
        Solicitar(outbox, abono, monto, motivo, ahora);
        return true;
    }
}
