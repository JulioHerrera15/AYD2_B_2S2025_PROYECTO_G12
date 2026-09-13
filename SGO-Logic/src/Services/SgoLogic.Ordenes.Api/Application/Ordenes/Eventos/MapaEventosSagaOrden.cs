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
/// LiberacionReservaSolicitada, sea cual sea el estado destino. El motivo de esa
/// liberacion lo puede fijar quien dispara la compensacion (por ejemplo, el
/// consumidor de Pagos distingue rechazo de sin-respuesta); si no lo fija, se
/// deriva del estado destino como antes.
///
/// Ordenes todavia no consume las respuestas de Inventario, asi que no conoce
/// reservaId ni tiendaDestinoId: esos campos, obligatorios en el contrato, se
/// rellenan aca con un valor derivado del id de la orden o con <c>null</c>, y
/// quedan marcados con <c>PENDIENTE</c> para cablearlos cuando exista el
/// consumidor de Inventario. pagoId y el CUI del receptor ya no son placeholder:
/// <see cref="ResolverPagoId" /> usa <see cref="Orden.PagoId" /> (persistido por
/// <see cref="Application.Ordenes.ManejadorEventosPagos" /> al consumir
/// PagoAplicado) y <see cref="Orden.ReceptorCui" /> viene de la orden desde su
/// creacion.
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
            [EstadoOrden.CONFIRMADA] = EmitirOrdenCertificadaFiscalmente,
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
    public static void Encolar(
        Orden orden,
        TransicionOrden transicion,
        IOutboxWriter outbox,
        MotivoLiberacionReserva? motivoLiberacion = null)
    {
        if (PorEstadoDestino.TryGetValue(transicion.EstadoNuevo, out var emisor))
        {
            emisor(orden, transicion, outbox);
        }

        if (transicion.EsCompensacion)
        {
            EmitirLiberacionReservaSolicitada(orden, transicion, outbox, motivoLiberacion);
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
                PagoId: ResolverPagoId(orden),
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
                // El CUI viene de la orden (item 8): "CF" si el cliente no dio uno
                // al crearla, o el que haya enviado. La direccion sigue PENDIENTE:
                // Ordenes no tiene un campo de direccion fiscal del receptor hoy.
                Receptor: new ReceptorFiscalData(orden.ReceptorCui, orden.ClienteId, "PENDIENTE", Pais.GT),
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

    /// <summary>
    /// Evento 12. Se emite al pasar a CONFIRMADA: la SAT certifico el documento
    /// (<see cref="ManejadorDocumentoFiscalCertificado" /> ya llamo a
    /// <see cref="Orden.AsignarCertificacionFiscal" /> antes de esta transicion,
    /// asi que los datos fiscales ya estan en la orden). Portador unico del folio
    /// fiscal; prohibido como fuente de estado (seccion 6 del contrato).
    /// </summary>
    private static void EmitirOrdenCertificadaFiscalmente(Orden orden, TransicionOrden transicion, IOutboxWriter outbox) =>
        Encolar(
            outbox,
            orden,
            OrdenCertificadaFiscalmente.EventType,
            OrdenCertificadaFiscalmente.RoutingKey,
            new OrdenCertificadaFiscalmenteData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                TiendaDestinoId: null,
                DocumentoFiscalId: orden.DocumentoFiscalId ?? string.Empty,
                TipoDocumento: TipoDocumentoFiscal.FACTURA,
                Serie: orden.DocumentoFiscalSerie ?? string.Empty,
                NumeroDocumento: orden.DocumentoFiscalNumero ?? string.Empty,
                NumeroAutorizacion: orden.DocumentoFiscalNumeroAutorizacion ?? string.Empty,
                Moneda: orden.Moneda,
                Total: orden.Total,
                CertificadaEn: orden.DocumentoFiscalCertificadoEn ?? transicion.CambiadoEn));

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
                PagoId: requiereReembolso ? ResolverPagoId(orden) : null,
                DocumentoFiscalId: null,
                Moneda: requiereReembolso ? orden.Moneda : null,
                MontoReembolso: requiereReembolso ? orden.Total : null,
                CanceladaEn: transicion.CambiadoEn));
    }

    private static void EmitirLiberacionReservaSolicitada(
        Orden orden,
        TransicionOrden transicion,
        IOutboxWriter outbox,
        MotivoLiberacionReserva? motivoExplicito) =>
        Encolar(
            outbox,
            orden,
            LiberacionReservaSolicitada.EventType,
            LiberacionReservaSolicitada.RoutingKey,
            new LiberacionReservaSolicitadaData(
                OrdenId: orden.Id,
                ReservaId: ReservaIdPendiente(orden),
                // El evento de origen manda: un rechazo de tarjeta si tuvo
                // respuesta del proveedor (COBRO_RECHAZADO). Sin motivo explicito
                // se cae al derivado del estado destino, que preserva el
                // comportamiento historico del resto de compensaciones.
                Motivo: motivoExplicito ?? MotivoSegunEstadoDestino(transicion.EstadoNuevo),
                SolicitadaEn: transicion.CambiadoEn));

    private static MotivoLiberacionReserva MotivoSegunEstadoDestino(EstadoOrden estadoNuevo) => estadoNuevo switch
    {
        EstadoOrden.PAGO_PENDIENTE => MotivoLiberacionReserva.COBRO_SIN_RESPUESTA,
        EstadoOrden.CANCELADA or EstadoOrden.CANCELADA_RESERVA_VENCIDA =>
            MotivoLiberacionReserva.CANCELACION_CLIENTE,
        _ => MotivoLiberacionReserva.TIMEOUT_SAGA
    };

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

    // reservaId todavia depende de Inventario, que Ordenes aun no consume.
    private static string ReservaIdPendiente(Orden orden) => $"RSV-PENDIENTE-{orden.Id}";

    // pagoId ya no es siempre placeholder: ManejadorEventosPagos lo persiste en
    // Orden.PagoId al consumir PagoAplicado (item 7). Antes de eso (por ejemplo,
    // si algo cancelara la orden sin haber pasado por ahi) se cae al placeholder.
    private static string ResolverPagoId(Orden orden) => orden.PagoId ?? $"PAG-PENDIENTE-{orden.Id}";
}
