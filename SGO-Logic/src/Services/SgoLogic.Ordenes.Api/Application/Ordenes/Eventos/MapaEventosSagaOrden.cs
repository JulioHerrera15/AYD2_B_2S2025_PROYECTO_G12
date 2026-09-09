using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;

/// <summary>
/// Mapeo explicito estado destino -&gt; evento de saga de ordenes-eventos-v1. Cubre
/// exactamente los estados que el contrato asocia a un evento; el resto de estados
/// de <see cref="EstadoOrden" /> solo produce OrdenEstadoCambiado y no aparece
/// aca. La compensacion (transicion marcada como tal) dispara ademas
/// LiberacionReservaSolicitada, sea cual sea el estado destino.
///
/// Ordenes todavia no consume las respuestas de Inventario, Pagos y Facturacion,
/// asi que no conoce reservaId, pagoId, documentoFiscalId, tiendaDestinoId ni los
/// datos fiscales del receptor. Esos campos, obligatorios en el contrato, se
/// rellenan aca con un valor derivado del id de la orden o con el valor de ejemplo
/// del contrato; quedan marcados con <c>PENDIENTE</c> para cablearlos cuando
/// existan los consumidores.
/// </summary>
public static class MapaEventosSagaOrden
{
    private const int TtlReservaSegundosPorDefecto = 900;

    private delegate void EmisorEvento(Orden orden, TransicionOrden transicion, IOutboxWriter outbox);

    private static readonly IReadOnlyDictionary<EstadoOrden, EmisorEvento> PorEstadoDestino =
        new Dictionary<EstadoOrden, EmisorEvento>
        {
            [EstadoOrden.RESERVA_SOLICITADA] = EmitirReservaInventarioSolicitada,
            [EstadoOrden.COBRO_SOLICITADO] = EmitirCobroSolicitado,
            [EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL] = EmitirConfirmacionYCertificacion,
            [EstadoOrden.DESPACHO_SOLICITADO] = EmitirDespachoSolicitado,
            [EstadoOrden.CANCELADA] = EmitirOrdenCancelada,
            [EstadoOrden.CANCELADA_RESERVA_VENCIDA] = EmitirOrdenCancelada
        };

    /// <summary>Estados destino que el contrato asocia a un evento de saga.</summary>
    public static IReadOnlySet<EstadoOrden> EstadosConEvento { get; } =
        PorEstadoDestino.Keys.ToHashSet();

    /// <summary>
    /// Encola en el outbox el evento (o eventos) de saga que corresponde a la
    /// transicion recien aplicada. No llama a SaveChangesAsync: eso lo hace el
    /// servicio en la misma transaccion que el cambio de negocio.
    /// </summary>
    public static void Encolar(Orden orden, TransicionOrden transicion, IOutboxWriter outbox)
    {
        if (PorEstadoDestino.TryGetValue(transicion.EstadoNuevo, out var emisor))
        {
            emisor(orden, transicion, outbox);
        }

        if (transicion.EsCompensacion)
        {
            EmitirLiberacionReservaSolicitada(orden, transicion, outbox);
        }
    }

    private static void EmitirReservaInventarioSolicitada(Orden orden, TransicionOrden transicion, IOutboxWriter outbox) =>
        Encolar(
            outbox,
            orden,
            ReservaInventarioSolicitada.EventType,
            ReservaInventarioSolicitada.RoutingKey,
            new ReservaInventarioSolicitadaData(
                OrdenId: orden.Id,
                MetodoEntrega: orden.MetodoEntrega,
                UbicacionPreferidaId: orden.UbicacionSolicitadaId,
                TtlReservaSegundos: TtlReservaSegundosPorDefecto,
                Lineas: orden.Lineas
                    .Select(linea => new ReservaInventarioLineaData(linea.Sku, linea.Cantidad))
                    .ToList(),
                SolicitadaEn: transicion.CambiadoEn));

    private static void EmitirCobroSolicitado(Orden orden, TransicionOrden transicion, IOutboxWriter outbox) =>
        Encolar(
            outbox,
            orden,
            CobroSolicitado.EventType,
            CobroSolicitado.RoutingKey,
            new CobroSolicitadoData(
                OrdenId: orden.Id,
                ClienteId: orden.ClienteId,
                Canal: orden.Canal,
                Moneda: orden.Moneda,
                Monto: orden.Total,
                MedioPago: orden.MedioPago,
                TokenMedioPago: null,
                UltimosCuatro: null,
                Marca: null,
                IntentoNumero: orden.Transiciones.Count(t => t.EstadoNuevo == EstadoOrden.COBRO_SOLICITADO),
                SolicitadoEn: transicion.CambiadoEn));

    private static void EmitirConfirmacionYCertificacion(Orden orden, TransicionOrden transicion, IOutboxWriter outbox)
    {
        Encolar(
            outbox,
            orden,
            OrdenConfirmada.EventType,
            OrdenConfirmada.RoutingKey,
            new OrdenConfirmadaData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                Canal: orden.Canal,
                TiendaDestinoId: null,
                Moneda: orden.Moneda,
                Subtotal: orden.Subtotal,
                Impuestos: orden.Impuestos,
                Total: orden.Total,
                PagoId: PagoIdPendiente(orden),
                DocumentoFiscalId: null,
                Lineas: orden.Lineas
                    .Select(linea => new OrdenConfirmadaLineaData(
                        linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, linea.Subtotal))
                    .ToList(),
                ConfirmadaEn: transicion.CambiadoEn));

        Encolar(
            outbox,
            orden,
            CertificacionFiscalSolicitada.EventType,
            CertificacionFiscalSolicitada.RoutingKey,
            new CertificacionFiscalSolicitadaData(
                OrdenId: orden.Id,
                TipoDocumento: TipoDocumentoFiscal.FACTURA,
                DocumentoOrigenId: null,
                Receptor: new ReceptorFiscalData("CF", orden.ClienteId, "PENDIENTE", Pais.GT),
                Moneda: orden.Moneda,
                Subtotal: orden.Subtotal,
                Impuestos: orden.Impuestos,
                Total: orden.Total,
                Lineas: orden.Lineas
                    .Select(linea => new CertificacionFiscalLineaData(
                        linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, 0m))
                    .ToList(),
                SolicitadaEn: transicion.CambiadoEn));
    }

    private static void EmitirDespachoSolicitado(Orden orden, TransicionOrden transicion, IOutboxWriter outbox) =>
        Encolar(
            outbox,
            orden,
            DespachoSolicitado.EventType,
            DespachoSolicitado.RoutingKey,
            new DespachoSolicitadoData(
                OrdenId: orden.Id,
                ReservaId: ReservaIdPendiente(orden),
                MetodoEntrega: orden.MetodoEntrega,
                UbicacionOrigenSugerida: null,
                UbicacionRecogidaId: orden.MetodoEntrega == MetodoEntrega.RECOGIDA_TIENDA
                    ? orden.UbicacionSolicitadaId
                    : null,
                DireccionEntrega: null,
                Lineas: orden.Lineas
                    .Select(linea => new DespachoLineaData(linea.Sku, linea.Descripcion, linea.Cantidad))
                    .ToList(),
                SolicitadoEn: transicion.CambiadoEn));

    private static void EmitirOrdenCancelada(Orden orden, TransicionOrden transicion, IOutboxWriter outbox)
    {
        var estabaConfirmada = orden.Transiciones.Any(t =>
            t.EstadoNuevo is EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL or EstadoOrden.CONFIRMADA);
        var requiereReembolso = orden.Transiciones.Any(t => t.EstadoNuevo == EstadoOrden.PAGO_APLICADO);

        Encolar(
            outbox,
            orden,
            OrdenCancelada.EventType,
            OrdenCancelada.RoutingKey,
            new OrdenCanceladaData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                TiendaDestinoId: null,
                EstabaConfirmada: estabaConfirmada,
                Motivo: transicion.EstadoNuevo == EstadoOrden.CANCELADA_RESERVA_VENCIDA
                    ? MotivoCancelacion.RESERVA_VENCIDA
                    : MotivoCancelacion.CLIENTE,
                Detalle: transicion.Motivo,
                RequiereReembolso: requiereReembolso,
                PagoId: requiereReembolso ? PagoIdPendiente(orden) : null,
                DocumentoFiscalId: null,
                Moneda: requiereReembolso ? orden.Moneda : null,
                MontoReembolso: requiereReembolso ? orden.Total : null,
                CanceladaEn: transicion.CambiadoEn));
    }

    private static void EmitirLiberacionReservaSolicitada(Orden orden, TransicionOrden transicion, IOutboxWriter outbox) =>
        Encolar(
            outbox,
            orden,
            LiberacionReservaSolicitada.EventType,
            LiberacionReservaSolicitada.RoutingKey,
            new LiberacionReservaSolicitadaData(
                OrdenId: orden.Id,
                ReservaId: ReservaIdPendiente(orden),
                Motivo: transicion.EstadoNuevo switch
                {
                    EstadoOrden.PAGO_PENDIENTE => MotivoLiberacionReserva.COBRO_SIN_RESPUESTA,
                    EstadoOrden.CANCELADA or EstadoOrden.CANCELADA_RESERVA_VENCIDA =>
                        MotivoLiberacionReserva.CANCELACION_CLIENTE,
                    _ => MotivoLiberacionReserva.TIMEOUT_SAGA
                },
                SolicitadaEn: transicion.CambiadoEn));

    private static void Encolar<TData>(
        IOutboxWriter outbox,
        Orden orden,
        string eventType,
        string routingKey,
        TData data)
        where TData : notnull =>
        outbox.Encolar(
            eventType,
            OrdenCreada.AggregateType,
            orden.Id,
            orden.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            routingKey,
            data);

    // Identificadores que hoy devuelven otros contextos y Ordenes aun no consume.
    private static string ReservaIdPendiente(Orden orden) => $"RSV-PENDIENTE-{orden.Id}";

    private static string PagoIdPendiente(Orden orden) => $"PAG-PENDIENTE-{orden.Id}";
}
