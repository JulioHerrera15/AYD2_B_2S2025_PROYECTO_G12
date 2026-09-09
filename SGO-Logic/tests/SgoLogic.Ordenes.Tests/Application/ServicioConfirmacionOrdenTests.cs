using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

public sealed class ServicioConfirmacionOrdenTests
{
    private const string OrdenId = "ORD-2026-000777";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL },
        ["CALL_CENTER"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA }
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

    private static CrearOrdenRequest CrearOrdenRequest(
        Canal canal = Canal.WEB,
        MedioPago medioPago = MedioPago.TARJETA) => new(
        ClienteId: "CLI-000045",
        Canal: canal,
        MetodoEntrega: MetodoEntrega.DOMICILIO,
        Moneda: Moneda.GTQ,
        MedioPago: medioPago,
        Impuestos: 150.00m,
        Lineas: new[] { new CrearLineaOrdenRequest("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) });

    private static Orden CrearOrdenEn(params EstadoOrden[] camino)
    {
        var orden = CrearOrden();
        foreach (var estado in camino)
        {
            orden.TransicionarA(estado);
        }

        return orden;
    }

    private static Orden CrearOrdenEntregada() => CrearOrdenEn(
        EstadoOrden.RESERVA_SOLICITADA,
        EstadoOrden.INVENTARIO_RESERVADO,
        EstadoOrden.COBRO_SOLICITADO,
        EstadoOrden.PAGO_APLICADO,
        EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
        EstadoOrden.CONFIRMADA,
        EstadoOrden.DESPACHO_SOLICITADO,
        EstadoOrden.EN_PREPARACION,
        EstadoOrden.EN_TRANSITO,
        EstadoOrden.ENTREGADA);

    private static (ServicioConfirmacionOrden Servicio,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad) Construir(long correlativoInicial = 777)
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioConfirmacionOrden(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoOrdenFalso(correlativoInicial),
            new PoliticaMediosPago(MediosPago));
        return (servicio, ordenes, outbox, unidad);
    }

    [Fact]
    public async Task Crear_orden_genera_id_con_formato_del_contrato_y_encola_OrdenCreada_con_sequence_uno()
    {
        var (servicio, _, outbox, unidad) = Construir(correlativoInicial: 42);

        var dto = await servicio.CrearOrdenAsync(CrearOrdenRequest(), CancellationToken.None);

        Assert.Matches(@"^ORD-\d{4}-\d{6}$", dto.Id);
        Assert.Equal($"ORD-{DateTime.UtcNow:yyyy}-000042", dto.Id);
        Assert.Equal("000042", dto.NumeroOrden);

        var evento = Assert.Single(outbox.Eventos);
        Assert.Equal(OrdenCreada.EventType, evento.EventType);
        Assert.Equal(OrdenCreada.RoutingKey, evento.RoutingKey);
        Assert.Equal(dto.Id, evento.AggregateId);
        Assert.Equal(1, evento.Sequence);
        var data = Assert.IsType<OrdenCreadaData>(evento.Data);
        Assert.Equal(dto.Id, data.OrdenId);
        Assert.Equal(EstadoOrden.CREADA, data.Estado);
        Assert.Equal(1400.00m, data.Total);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Crear_orden_con_medio_de_pago_habilitado_persiste_el_medio_elegido()
    {
        var (servicio, _, outbox, _) = Construir();

        var dto = await servicio.CrearOrdenAsync(
            CrearOrdenRequest(Canal.WEB, MedioPago.TRANSFERENCIA), CancellationToken.None);

        Assert.Equal(MedioPago.TRANSFERENCIA, dto.MedioPago);
        var data = Assert.IsType<OrdenCreadaData>(Assert.Single(outbox.Eventos).Data);
        Assert.Equal(dto.Id, data.OrdenId);
    }

    [Fact]
    public async Task Crear_orden_con_medio_de_pago_no_habilitado_para_el_canal_lanza_y_no_escribe()
    {
        var (servicio, _, outbox, unidad) = Construir();

        var excepcion = await Assert.ThrowsAsync<ExcepcionMedioPagoNoHabilitado>(() =>
            servicio.CrearOrdenAsync(
                CrearOrdenRequest(Canal.CALL_CENTER, MedioPago.BILLETERA_DIGITAL), CancellationToken.None));

        Assert.Equal(Canal.CALL_CENTER, excepcion.Canal);
        Assert.Equal(MedioPago.BILLETERA_DIGITAL, excepcion.MedioPago);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task El_evento_CobroSolicitado_lleva_el_medio_de_pago_real_de_la_orden()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.INVENTARIO_RESERVADO);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id, EstadoOrden.COBRO_SOLICITADO, PasoSaga.COBRO, null, false, CancellationToken.None);

        var cobro = Assert.Single(outbox.PorTipo(CobroSolicitado.EventType));
        var data = Assert.IsType<CobroSolicitadoData>(cobro.Data);
        Assert.Equal(orden.MedioPago, data.MedioPago);
    }

    [Fact]
    public async Task Confirmar_orden_en_estado_valido_encola_estado_cambiado_y_evento_de_saga()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        var resultado = await servicio.ConfirmarOrdenAsync(orden.Id, "Cliente confirmo la compra", CancellationToken.None);

        Assert.False(resultado.YaConfirmada);
        Assert.Equal(EstadoOrden.RESERVA_SOLICITADA, orden.Estado);
        Assert.Single(orden.Transiciones);

        var estadoCambiado = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(OrdenEstadoCambiado.RoutingKey, estadoCambiado.RoutingKey);
        var data = Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data);
        Assert.Equal(EstadoOrden.CREADA, data.EstadoAnterior);
        Assert.Equal(EstadoOrden.RESERVA_SOLICITADA, data.EstadoNuevo);
        Assert.Equal("CLI-000045", data.ClienteId);

        var reserva = Assert.Single(outbox.PorTipo(ReservaInventarioSolicitada.EventType));
        Assert.Equal(ReservaInventarioSolicitada.RoutingKey, reserva.RoutingKey);
        Assert.IsType<ReservaInventarioSolicitadaData>(reserva.Data);

        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Confirmar_con_transicion_invalida_no_encola_nada_y_lanza_la_excepcion()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.INVENTARIO_RESERVADO);
        ordenes.Precargar(orden);
        var transicionesAntes = orden.Transiciones.Count;

        await Assert.ThrowsAsync<ExcepcionTransicionInvalida>(
            () => servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None));

        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Equal(EstadoOrden.INVENTARIO_RESERVADO, orden.Estado);
    }

    [Fact]
    public async Task Confirmar_dos_veces_la_misma_orden_no_duplica_transicion_ni_eventos()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        var primera = await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);
        var transicionesTrasPrimera = orden.Transiciones.Count;
        var eventosTrasPrimera = outbox.Eventos.Count;

        var segunda = await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);

        Assert.False(primera.YaConfirmada);
        Assert.True(segunda.YaConfirmada);
        Assert.Equal(transicionesTrasPrimera, orden.Transiciones.Count);
        Assert.Equal(eventosTrasPrimera, outbox.Eventos.Count);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task El_evento_de_estado_cambiado_lleva_el_mismo_agregado_y_secuencia_que_la_transicion()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);

        var estadoCambiado = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(orden.Id, estadoCambiado.AggregateId);
        Assert.Equal("Orden", estadoCambiado.AggregateType);
        Assert.Equal(orden.Secuencia, estadoCambiado.Sequence);
        var data = Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data);
        Assert.Equal(orden.Id, data.OrdenId);
    }

    [Fact]
    public async Task Confirmar_una_orden_ya_entregada_lanza_ExcepcionTransicionInvalida()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEntregada();
        ordenes.Precargar(orden);
        var transicionesAntes = orden.Transiciones.Count;

        await Assert.ThrowsAsync<ExcepcionTransicionInvalida>(
            () => servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None));

        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Theory]
    [InlineData(EstadoOrden.RESERVA_SOLICITADA, "ReservaInventarioSolicitada", "ordenes.reserva.solicitada")]
    [InlineData(EstadoOrden.COBRO_SOLICITADO, "CobroSolicitado", "ordenes.cobro.solicitado")]
    [InlineData(EstadoOrden.DESPACHO_SOLICITADO, "DespachoSolicitado", "ordenes.despacho.solicitado")]
    [InlineData(EstadoOrden.CANCELADA, "OrdenCancelada", "ordenes.orden.cancelada")]
    [InlineData(EstadoOrden.CANCELADA_RESERVA_VENCIDA, "OrdenCancelada", "ordenes.orden.cancelada")]
    public async Task Cada_estado_destino_del_contrato_encola_su_evento_de_saga(
        EstadoOrden estadoDestino,
        string eventTypeEsperado,
        string routingKeyEsperada)
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = LlevarHasta(estadoDestino);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id, estadoDestino, pasoSaga: null, motivo: null, esCompensacion: false, CancellationToken.None);

        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        var saga = Assert.Single(outbox.PorTipo(eventTypeEsperado));
        Assert.Equal(routingKeyEsperada, saga.RoutingKey);
        Assert.Equal(orden.Id, saga.AggregateId);
        Assert.Equal(orden.Secuencia, saga.Sequence);
    }

    [Fact]
    public async Task Estado_destino_a_CONFIRMADA_PENDIENTE_FISCAL_encola_confirmada_y_certificacion()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEn(
            EstadoOrden.RESERVA_SOLICITADA,
            EstadoOrden.INVENTARIO_RESERVADO,
            EstadoOrden.COBRO_SOLICITADO,
            EstadoOrden.PAGO_APLICADO);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id,
            EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
            PasoSaga.FISCAL,
            motivo: null,
            esCompensacion: false,
            CancellationToken.None);

        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        var confirmada = Assert.Single(outbox.PorTipo(OrdenConfirmada.EventType));
        Assert.Equal(OrdenConfirmada.RoutingKey, confirmada.RoutingKey);
        Assert.IsType<OrdenConfirmadaData>(confirmada.Data);
        var certificacion = Assert.Single(outbox.PorTipo(CertificacionFiscalSolicitada.EventType));
        Assert.Equal(CertificacionFiscalSolicitada.RoutingKey, certificacion.RoutingKey);
        Assert.IsType<CertificacionFiscalSolicitadaData>(certificacion.Data);

        // Los tres eventos van en una sola transaccion de base de datos.
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Estado_destino_sin_evento_de_saga_solo_encola_estado_cambiado()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id, EstadoOrden.INVENTARIO_RESERVADO, PasoSaga.RESERVA, null, false, CancellationToken.None);

        var evento = Assert.Single(outbox.Eventos);
        Assert.Equal(OrdenEstadoCambiado.EventType, evento.EventType);
    }

    [Fact]
    public async Task Una_transicion_marcada_como_compensacion_encola_LiberacionReservaSolicitada()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrdenEn(
            EstadoOrden.RESERVA_SOLICITADA,
            EstadoOrden.INVENTARIO_RESERVADO,
            EstadoOrden.COBRO_SOLICITADO);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id,
            EstadoOrden.PAGO_PENDIENTE,
            PasoSaga.COBRO,
            "PayKlik no respondio dentro del timeout configurado",
            esCompensacion: true,
            CancellationToken.None);

        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        var liberacion = Assert.Single(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        Assert.Equal(LiberacionReservaSolicitada.RoutingKey, liberacion.RoutingKey);
        var data = Assert.IsType<LiberacionReservaSolicitadaData>(liberacion.Data);
        Assert.Equal(orden.Id, data.OrdenId);
        Assert.Equal(MotivoLiberacionReserva.COBRO_SIN_RESPUESTA, data.Motivo);
    }

    [Fact]
    public async Task Aplicar_transicion_generica_avanza_la_maquina_de_estados_y_deja_evento()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA);
        ordenes.Precargar(orden);

        var resultado = await servicio.AplicarTransicionAsync(
            orden.Id,
            EstadoOrden.INVENTARIO_RESERVADO,
            PasoSaga.RESERVA,
            motivo: null,
            esCompensacion: false,
            CancellationToken.None);

        Assert.False(resultado.SinCambios);
        Assert.Equal(EstadoOrden.INVENTARIO_RESERVADO, orden.Estado);
        var evento = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(orden.Id, evento.AggregateId);
        Assert.Equal(orden.Secuencia, evento.Sequence);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Cambiar_el_metodo_de_entrega_encola_MetodoEntregaModificado_en_una_sola_transaccion()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA);
        ordenes.Precargar(orden);

        var resultado = await servicio.CambiarMetodoEntregaAsync(
            orden.Id,
            new CambiarMetodoEntregaRequest(
                MetodoEntregaNuevo: MetodoEntrega.RECOGIDA_TIENDA,
                SolicitadoPor: SolicitanteCambio.CLIENTE,
                UbicacionRecogidaId: "TDA-014"),
            CancellationToken.None);

        Assert.False(resultado.SinCambios);
        Assert.Equal(MetodoEntrega.RECOGIDA_TIENDA, orden.MetodoEntrega);
        var evento = Assert.Single(outbox.Eventos);
        Assert.Equal(MetodoEntregaModificado.EventType, evento.EventType);
        Assert.Equal(MetodoEntregaModificado.RoutingKey, evento.RoutingKey);
        var data = Assert.IsType<MetodoEntregaModificadoData>(evento.Data);
        Assert.Equal(MetodoEntrega.DOMICILIO, data.MetodoEntregaAnterior);
        Assert.Equal(MetodoEntrega.RECOGIDA_TIENDA, data.MetodoEntregaNuevo);
        Assert.Equal("TDA-014", data.UbicacionRecogidaId);
        Assert.Equal(SolicitanteCambio.CLIENTE, data.SolicitadoPor);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Cambiar_el_metodo_de_entrega_al_mismo_valor_no_encola_ni_escribe()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        var resultado = await servicio.CambiarMetodoEntregaAsync(
            orden.Id,
            new CambiarMetodoEntregaRequest(MetodoEntrega.DOMICILIO, SolicitanteCambio.AGENTE),
            CancellationToken.None);

        Assert.True(resultado.SinCambios);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Cambiar_el_metodo_de_entrega_de_una_orden_inexistente_lanza()
    {
        var (servicio, _, outbox, unidad) = Construir();

        await Assert.ThrowsAsync<ExcepcionOrdenNoEncontrada>(() => servicio.CambiarMetodoEntregaAsync(
            "ORD-NO-EXISTE",
            new CambiarMetodoEntregaRequest(MetodoEntrega.RECOGIDA_TIENDA, SolicitanteCambio.CLIENTE, "TDA-014"),
            CancellationToken.None));

        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    private static Orden LlevarHasta(EstadoOrden estadoDestino) => estadoDestino switch
    {
        EstadoOrden.RESERVA_SOLICITADA => CrearOrden(),
        EstadoOrden.COBRO_SOLICITADO => CrearOrdenEn(
            EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.INVENTARIO_RESERVADO),
        EstadoOrden.DESPACHO_SOLICITADO => CrearOrdenEn(
            EstadoOrden.RESERVA_SOLICITADA,
            EstadoOrden.INVENTARIO_RESERVADO,
            EstadoOrden.COBRO_SOLICITADO,
            EstadoOrden.PAGO_APLICADO,
            EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
            EstadoOrden.CONFIRMADA),
        EstadoOrden.CANCELADA => CrearOrden(),
        EstadoOrden.CANCELADA_RESERVA_VENCIDA => CrearOrdenEn(
            EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.INVENTARIO_RESERVADO),
        _ => throw new ArgumentOutOfRangeException(nameof(estadoDestino), estadoDestino, null)
    };
}
