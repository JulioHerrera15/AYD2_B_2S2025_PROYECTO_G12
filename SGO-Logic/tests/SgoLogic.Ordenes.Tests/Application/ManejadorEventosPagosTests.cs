using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Pruebas del avance de la saga de Ordenes al consumir los resultados de Pagos y
/// de la compensacion ante falla de cobro (HU-003). El manejador reutiliza
/// <see cref="ServicioConfirmacionOrden" />, asi que estas pruebas lo arman con los
/// mismos dobles en memoria que el resto del contexto.
/// </summary>
public sealed class ManejadorEventosPagosTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string TransactionId = "a1c4e8b2-77d3-4f19-b5aa-0e93c62d1845";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private static Orden CrearOrden() => new(
        OrdenId,
        "000777",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        150.00m);

    private static Orden OrdenEnCobroSolicitado()
    {
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-2026-000042", "CD-01");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        return orden;
    }

    private static EventoPagoRecibido Evento(string tipoEvento, string messageId, string? motivo = null) =>
        new(messageId, TransactionId, tipoEvento, OrdenId, motivo);

    private static EventoPagoRecibido Aplicado(string messageId = "EVT-APLICADO") =>
        Evento(PagoAplicado.EventType, messageId);

    private static EventoPagoRecibido Rechazado(string messageId = "EVT-RECHAZADO") =>
        Evento(PagoRechazado.EventType, messageId, "Cobro rechazado por el proveedor (codigo 51)");

    private static EventoPagoRecibido Pendiente(string messageId = "EVT-PENDIENTE") =>
        Evento(PagoPendiente.EventType, messageId, "PayKlik no respondio dentro de los 5000 ms configurados");

    private static (ManejadorEventosPagos Manejador,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad,
        RegistroMensajesProcesadosFalso Registro) Construir()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var registro = new RegistroMensajesProcesadosFalso();
        var servicio = new ServicioConfirmacionOrden(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso());
        var manejador = new ManejadorEventosPagos(
            registro,
            servicio,
            ordenes,
            unidad,
            NullLogger<ManejadorEventosPagos>.Instance);
        return (manejador, ordenes, outbox, unidad, registro);
    }

    [Fact]
    public async Task El_mismo_messageId_dos_veces_aplica_la_transicion_una_sola_vez()
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        var primera = await manejador.ManejarAsync(Aplicado("EVT-1"), CancellationToken.None);
        var segunda = await manejador.ManejarAsync(Aplicado("EVT-1"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, primera);
        Assert.Equal(ResultadoEventoPago.Duplicado, segunda);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, orden.Estado);
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.PAGO_APLICADO));
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL));
        Assert.Equal(2, outbox.PorTipo(OrdenEstadoCambiado.EventType).Count());
        Assert.Single(outbox.PorTipo(CertificacionFiscalSolicitada.EventType));
        Assert.Single(outbox.PorTipo(OrdenConfirmada.EventType));
        Assert.Equal(1, unidad.Confirmaciones);
        Assert.Single(registro.Registrados);
        Assert.Equal(PagoAplicado.EventType, registro.Registrados[0].TipoEvento);
    }

    [Fact]
    public async Task PagoAplicado_lleva_la_orden_a_CONFIRMADA_PENDIENTE_FISCAL_en_una_sola_transaccion()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Aplicado(), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, orden.Estado);

        var cambios = outbox.PorTipo(OrdenEstadoCambiado.EventType)
            .Select(e => Assert.IsType<OrdenEstadoCambiadoData>(e.Data))
            .ToList();
        Assert.Equal(2, cambios.Count);

        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, cambios[0].EstadoAnterior);
        Assert.Equal(EstadoOrden.PAGO_APLICADO, cambios[0].EstadoNuevo);
        Assert.Equal(PasoSaga.COBRO, cambios[0].PasoSaga);
        Assert.False(cambios[0].EsCompensacion);

        Assert.Equal(EstadoOrden.PAGO_APLICADO, cambios[1].EstadoAnterior);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, cambios[1].EstadoNuevo);
        Assert.Equal(PasoSaga.FISCAL, cambios[1].PasoSaga);
        Assert.False(cambios[1].EsCompensacion);

        Assert.Empty(outbox.PorTipo(LiberacionReservaSolicitada.EventType));

        // Las dos transiciones y todas las filas de outbox salen de un solo SaveChanges.
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task PagoAplicado_emite_a_Facturacion_lo_mismo_que_la_transicion_forzada()
    {
        var (manejador, ordenes, outbox, _, _) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(Aplicado(), CancellationToken.None);

        var certificacion = Assert.Single(outbox.PorTipo(CertificacionFiscalSolicitada.EventType));
        Assert.Equal(CertificacionFiscalSolicitada.RoutingKey, certificacion.RoutingKey);
        Assert.Equal(orden.Id, certificacion.AggregateId);
        Assert.Equal(orden.Secuencia, certificacion.Sequence);
        var data = Assert.IsType<CertificacionFiscalSolicitadaData>(certificacion.Data);
        Assert.Equal(orden.Id, data.OrdenId);
        Assert.Equal(1400.00m, data.Total);

        var confirmada = Assert.Single(outbox.PorTipo(OrdenConfirmada.EventType));
        Assert.Equal(OrdenConfirmada.RoutingKey, confirmada.RoutingKey);

        // Referencia: la misma orden llevada a CONFIRMADA_PENDIENTE_FISCAL por el
        // camino del endpoint de transiciones encola exactamente esos tres eventos.
        var (_, ordenesForzada, outboxForzada, unidadForzada, _) = Construir();
        var ordenForzada = OrdenEnCobroSolicitado();
        ordenForzada.TransicionarA(EstadoOrden.PAGO_APLICADO);
        ordenesForzada.Precargar(ordenForzada);
        var servicio = new ServicioConfirmacionOrden(
            ordenesForzada,
            outboxForzada,
            unidadForzada,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso());

        await servicio.AplicarTransicionAsync(
            ordenForzada.Id,
            EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
            PasoSaga.FISCAL,
            motivo: null,
            esCompensacion: false,
            CancellationToken.None);

        Assert.Equal(
            outboxForzada.Eventos.Select(e => (e.EventType, e.RoutingKey)).ToArray(),
            outbox.Eventos.Skip(1).Select(e => (e.EventType, e.RoutingKey)).ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Un_pago_que_no_se_aplico_no_solicita_certificacion_fiscal(bool rechazado)
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        var evento = rechazado ? Rechazado() : Pendiente();
        var resultado = await manejador.ManejarAsync(evento, CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoOrden.PAGO_PENDIENTE, orden.Estado);
        Assert.DoesNotContain(orden.Transiciones, t => t.EstadoNuevo == EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        Assert.Empty(outbox.PorTipo(CertificacionFiscalSolicitada.EventType));
        Assert.Empty(outbox.PorTipo(OrdenConfirmada.EventType));
        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task PagoRechazado_emite_la_liberacion_de_reserva_con_COBRO_RECHAZADO()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Rechazado(), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoOrden.PAGO_PENDIENTE, orden.Estado);

        var estadoCambiado = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.True(Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data).EsCompensacion);

        var liberacion = Assert.Single(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        Assert.Equal(LiberacionReservaSolicitada.RoutingKey, liberacion.RoutingKey);
        var data = Assert.IsType<LiberacionReservaSolicitadaData>(liberacion.Data);
        Assert.Equal(orden.Id, data.OrdenId);
        // Un rechazo de tarjeta si tuvo respuesta del proveedor.
        Assert.Equal(MotivoLiberacionReserva.COBRO_RECHAZADO, data.Motivo);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task PagoPendiente_emite_la_liberacion_de_reserva_con_COBRO_SIN_RESPUESTA()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Pendiente(), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoOrden.PAGO_PENDIENTE, orden.Estado);

        var estadoCambiado = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.True(Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data).EsCompensacion);

        var liberacion = Assert.Single(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        var data = Assert.IsType<LiberacionReservaSolicitadaData>(liberacion.Data);
        Assert.Equal(orden.Id, data.OrdenId);
        // Timeout, 5xx o red: el cobro quedo sin respuesta del proveedor.
        Assert.Equal(MotivoLiberacionReserva.COBRO_SIN_RESPUESTA, data.Motivo);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Una_transicion_invalida_no_lanza_excepcion_hacia_afuera_del_consumidor()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = CrearOrden(); // CREADA: no admite pasar a PAGO_APLICADO
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Aplicado(), CancellationToken.None);

        // No se propaga excepcion: el await habria lanzado de ser asi.
        Assert.Equal(ResultadoEventoPago.Ignorado, resultado);
        Assert.Equal(EstadoOrden.CREADA, orden.Estado);
        Assert.Empty(outbox.Eventos);
        // Nada se persiste: AplicarTransicionAsync lanzo antes del SaveChanges.
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Un_evento_de_Pagos_desconocido_se_ignora_con_ack_y_sin_tocar_la_orden()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(
            Evento("PagoReembolsado", "EVT-RARO"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Ignorado, resultado);
        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Un_messageId_ya_registrado_antes_no_reaplica_la_transicion()
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEnCobroSolicitado();
        ordenes.Precargar(orden);
        registro.Precargar("EVT-YA-VISTO");

        var resultado = await manejador.ManejarAsync(Aplicado("EVT-YA-VISTO"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Duplicado, resultado);
        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }
}
