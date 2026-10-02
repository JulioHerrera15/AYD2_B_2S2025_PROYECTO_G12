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

    private static readonly DireccionEntregaData DireccionDePrueba = new(
        Pais.GT, "Guatemala", "San Juan Sacatepequez", "5a avenida 3-21 zona 2", "Porton azul");

    private static Orden CrearOrdenA(DireccionEntregaData direccion) => new(
        OrdenId,
        "000777",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        150.00m,
        direccionEntrega: direccion);

    /// <summary>Lleva la orden hasta CONFIRMADA con reserva registrada, como en la saga real.</summary>
    private static void AvanzarHastaConfirmada(Orden orden)
    {
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-2026-000042", "CD-01");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
    }

    private static CrearOrdenRequest CrearOrdenRequest(
        Canal canal = Canal.WEB,
        string medioPago = "TARJETA") => new(
        ClienteId: "CLI-000045",
        Canal: canal,
        MetodoEntrega: MetodoEntrega.DOMICILIO,
        Moneda: Moneda.GTQ,
        MedioPago: medioPago,
        Impuestos: 150.00m,
        Lineas: new[] { new CrearLineaOrdenRequest("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        // La tarjeta exige el token que emitio PayKlik (ADR-010); los demas medios no lo llevan.
        TokenMedioPago: medioPago == "TARJETA" ? "tok_pk_9f2a7c41d8e0b3a6c5d4e1f2" : null);

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
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso(),
            new CatalogoProductosFalso());
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
            CrearOrdenRequest(Canal.WEB, "TRANSFERENCIA"), CancellationToken.None);

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
                CrearOrdenRequest(Canal.CALL_CENTER, "BILLETERA_DIGITAL"), CancellationToken.None));

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
        orden.RegistrarReserva("RSV-2026-000042", "CD-01");
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
        Assert.Equal("RSV-2026-000042", data.ReservaId);
        Assert.Equal(MotivoLiberacionReserva.COBRO_SIN_RESPUESTA, data.Motivo);
    }

    [Theory]
    [InlineData(EstadoOrden.CANCELADA)]
    [InlineData(EstadoOrden.RECHAZADA)]
    public async Task Una_compensacion_sin_reserva_conocida_no_publica_LiberacionReservaSolicitada(
        EstadoOrden estadoDestino)
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id,
            estadoDestino,
            PasoSaga.RESERVA,
            "Cancelada antes de InventarioReservado",
            esCompensacion: true,
            CancellationToken.None);

        // Sin reservaId no hay nada que liberar y no se inventa uno: la
        // expiracion por TTL de Inventario cubre una reserva tardia.
        Assert.Equal(estadoDestino, orden.Estado);
        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Empty(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task DespachoSolicitado_a_domicilio_lleva_el_reservaId_real_y_la_direccion_de_entrega()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrdenA(DireccionDePrueba);
        AvanzarHastaConfirmada(orden);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id, EstadoOrden.DESPACHO_SOLICITADO, PasoSaga.DESPACHO, null, false, CancellationToken.None);

        var evento = Assert.Single(outbox.PorTipo(DespachoSolicitado.EventType));
        Assert.Equal(DespachoSolicitado.RoutingKey, evento.RoutingKey);
        var data = Assert.IsType<DespachoSolicitadoData>(evento.Data);
        Assert.Equal(orden.Id, data.OrdenId);
        Assert.Equal("RSV-2026-000042", data.ReservaId);
        Assert.Equal(MetodoEntrega.DOMICILIO, data.MetodoEntrega);
        Assert.Null(data.UbicacionRecogidaId);
        Assert.Equal(DireccionDePrueba, data.DireccionEntrega);
        var linea = Assert.Single(data.Lineas);
        Assert.Equal("SKU-LB-00871", linea.Sku);
        Assert.Equal(1, linea.Cantidad);
    }

    [Fact]
    public async Task DespachoSolicitado_de_recogida_lleva_la_tienda_y_no_lleva_direccion()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = new Orden(
            OrdenId,
            "000777",
            "CLI-000045",
            Canal.WEB,
            MetodoEntrega.RECOGIDA_TIENDA,
            Moneda.GTQ,
            MedioPago.TARJETA,
            new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
            150.00m,
            ubicacionSolicitadaId: "TDA-014",
            direccionEntrega: DireccionDePrueba);
        AvanzarHastaConfirmada(orden);
        ordenes.Precargar(orden);

        await servicio.AplicarTransicionAsync(
            orden.Id, EstadoOrden.DESPACHO_SOLICITADO, PasoSaga.DESPACHO, null, false, CancellationToken.None);

        var data = Assert.IsType<DespachoSolicitadoData>(
            Assert.Single(outbox.PorTipo(DespachoSolicitado.EventType)).Data);
        Assert.Equal(MetodoEntrega.RECOGIDA_TIENDA, data.MetodoEntrega);
        Assert.Equal("TDA-014", data.UbicacionRecogidaId);
        Assert.Null(data.DireccionEntrega);
    }

    [Fact]
    public async Task Crear_orden_a_domicilio_con_direccion_la_conserva_y_la_incluye_en_OrdenCreada()
    {
        var (servicio, ordenes, outbox, _) = Construir();

        var dto = await servicio.CrearOrdenAsync(
            CrearOrdenRequest() with { DireccionEntrega = DireccionDePrueba }, CancellationToken.None);

        var orden = await ordenes.ObtenerPorIdAsync(dto.Id, CancellationToken.None);
        Assert.Equal(DireccionDePrueba, orden!.DireccionEntrega);
        var data = Assert.IsType<OrdenCreadaData>(Assert.Single(outbox.Eventos).Data);
        Assert.Equal(DireccionDePrueba, data.DireccionEntrega);
    }

    [Fact]
    public async Task Crear_orden_sin_direccion_sigue_funcionando_y_emite_direccion_nula()
    {
        var (servicio, _, outbox, _) = Construir();

        await servicio.CrearOrdenAsync(CrearOrdenRequest(), CancellationToken.None);

        var data = Assert.IsType<OrdenCreadaData>(Assert.Single(outbox.Eventos).Data);
        Assert.Null(data.DireccionEntrega);
    }

    [Fact]
    public async Task Crear_orden_con_direccion_incompleta_lanza_ArgumentException_y_no_escribe()
    {
        var (servicio, _, outbox, unidad) = Construir();

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearOrdenAsync(
            CrearOrdenRequest() with { DireccionEntrega = DireccionDePrueba with { Municipio = " " } },
            CancellationToken.None));

        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Cambiar_a_domicilio_conserva_la_direccion_y_la_manda_en_MetodoEntregaModificado()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = new Orden(
            OrdenId,
            "000777",
            "CLI-000045",
            Canal.WEB,
            MetodoEntrega.RECOGIDA_TIENDA,
            Moneda.GTQ,
            MedioPago.TARJETA,
            new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
            150.00m,
            ubicacionSolicitadaId: "TDA-014");
        ordenes.Precargar(orden);

        await servicio.CambiarMetodoEntregaAsync(
            orden.Id,
            new CambiarMetodoEntregaRequest(
                MetodoEntrega.DOMICILIO, SolicitanteCambio.CLIENTE, DireccionEntrega: DireccionDePrueba),
            CancellationToken.None);

        Assert.Equal(DireccionDePrueba, orden.DireccionEntrega);
        Assert.Null(orden.UbicacionSolicitadaId);
        var data = Assert.IsType<MetodoEntregaModificadoData>(Assert.Single(outbox.Eventos).Data);
        Assert.Equal(DireccionDePrueba, data.DireccionEntrega);
    }

    [Fact]
    public async Task Cambiar_a_recogida_borra_la_direccion_de_la_orden()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrdenA(DireccionDePrueba);
        ordenes.Precargar(orden);

        await servicio.CambiarMetodoEntregaAsync(
            orden.Id,
            new CambiarMetodoEntregaRequest(
                MetodoEntrega.RECOGIDA_TIENDA, SolicitanteCambio.CLIENTE, "TDA-014"),
            CancellationToken.None);

        Assert.Null(orden.DireccionEntrega);
        var data = Assert.IsType<MetodoEntregaModificadoData>(Assert.Single(outbox.Eventos).Data);
        Assert.Null(data.DireccionEntrega);
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

    // ── Cancelacion por el cliente ─────────────────────────────────────────────

    private static CancelarOrdenRequest SolicitudCancelacion(string clienteId = "CLI-000045") =>
        new(clienteId, "Ya no lo necesito");

    [Fact]
    public async Task Cancelar_una_orden_ajena_lanza_orden_ajena_sin_escribir()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        ordenes.Precargar(CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA));

        var ex = await Assert.ThrowsAsync<ExcepcionOrdenAjena>(() => servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion("otro@sgo.local"), CancellationToken.None));

        Assert.Equal("La orden no pertenece al cliente", ex.Message);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Cancelar_compara_el_cliente_sin_distinguir_mayusculas()
    {
        var (servicio, ordenes, _, _) = Construir();
        ordenes.Precargar(CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA));

        var resultado = await servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion("cli-000045"), CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, resultado.Orden.Estado);
    }

    [Fact]
    public async Task Cancelar_una_orden_inexistente_lanza_no_encontrada()
    {
        var (servicio, _, _, _) = Construir();

        await Assert.ThrowsAsync<ExcepcionOrdenNoEncontrada>(() => servicio.CancelarPorClienteAsync(
            "ORD-2026-999999", SolicitudCancelacion(), CancellationToken.None));
    }

    [Theory]
    [InlineData(EstadoOrden.COBRO_SOLICITADO)]
    [InlineData(EstadoOrden.PAGO_APLICADO)]
    public async Task Cancelar_con_el_cobro_en_curso_lanza_pago_en_curso(EstadoOrden estado)
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var camino = new List<EstadoOrden>
        {
            EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.INVENTARIO_RESERVADO, EstadoOrden.COBRO_SOLICITADO
        };
        if (estado == EstadoOrden.PAGO_APLICADO)
        {
            camino.Add(EstadoOrden.PAGO_APLICADO);
        }
        ordenes.Precargar(CrearOrdenEn(camino.ToArray()));

        var ex = await Assert.ThrowsAsync<ExcepcionCancelacionNoPermitida>(() => servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion(), CancellationToken.None));

        Assert.True(ex.PagoEnCurso);
        Assert.Equal("La orden esta procesando el pago, intenta de nuevo en unos segundos", ex.Message);
        Assert.Empty(outbox.Eventos);
    }

    [Fact]
    public async Task Cancelar_una_orden_en_transito_lanza_no_cancelable()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        ordenes.Precargar(CrearOrdenEn(
            EstadoOrden.RESERVA_SOLICITADA,
            EstadoOrden.INVENTARIO_RESERVADO,
            EstadoOrden.COBRO_SOLICITADO,
            EstadoOrden.PAGO_APLICADO,
            EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
            EstadoOrden.CONFIRMADA,
            EstadoOrden.DESPACHO_SOLICITADO,
            EstadoOrden.EN_PREPARACION,
            EstadoOrden.EN_TRANSITO));

        var ex = await Assert.ThrowsAsync<ExcepcionCancelacionNoPermitida>(() => servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion(), CancellationToken.None));

        Assert.False(ex.PagoEnCurso);
        Assert.Equal("La orden ya no puede cancelarse", ex.Message);
        Assert.Empty(outbox.Eventos);
    }

    [Fact]
    public async Task Cancelar_una_orden_ya_cancelada_lanza_no_cancelable()
    {
        var (servicio, ordenes, _, _) = Construir();
        ordenes.Precargar(CrearOrdenEn(EstadoOrden.CANCELADA));

        var ex = await Assert.ThrowsAsync<ExcepcionCancelacionNoPermitida>(() => servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion(), CancellationToken.None));

        Assert.False(ex.PagoEnCurso);
    }

    [Fact]
    public async Task Cancelar_con_reserva_pasa_a_CANCELADA_y_pide_liberar_la_reserva()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-2026-000042", "CD-01");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        ordenes.Precargar(orden);

        var resultado = await servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion(), CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, resultado.Orden.Estado);
        Assert.True(orden.Transiciones.Last().EsCompensacion);
        Assert.Equal(1, unidad.Confirmaciones);

        var liberacion = Assert.Single(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        var datosLiberacion = Assert.IsType<LiberacionReservaSolicitadaData>(liberacion.Data);
        Assert.Equal(OrdenId, datosLiberacion.OrdenId);
        Assert.Equal("RSV-2026-000042", datosLiberacion.ReservaId);
        Assert.Equal(MotivoLiberacionReserva.CANCELACION_CLIENTE, datosLiberacion.Motivo);

        var cancelada = Assert.Single(outbox.PorTipo(OrdenCancelada.EventType));
        var datosCancelada = Assert.IsType<OrdenCanceladaData>(cancelada.Data);
        Assert.Equal(MotivoCancelacion.CLIENTE, datosCancelada.Motivo);
        Assert.False(datosCancelada.RequiereReembolso);
    }

    [Fact]
    public async Task Cancelar_despues_del_pago_marca_que_requiere_reembolso()
    {
        var (servicio, ordenes, outbox, _) = Construir();
        var orden = CrearOrden();
        AvanzarHastaConfirmada(orden);
        ordenes.Precargar(orden);

        await servicio.CancelarPorClienteAsync(OrdenId, SolicitudCancelacion(), CancellationToken.None);

        var datos = Assert.IsType<OrdenCanceladaData>(Assert.Single(outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.True(datos.RequiereReembolso);
        Assert.Single(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
    }

    [Fact]
    public async Task Cancelar_sin_reserva_aun_no_pide_liberar()
    {
        // RESERVA_SOLICITADA: la reserva va en camino y todavia no hay ReservaId. La
        // libera ManejadorEventosInventario cuando llegue InventarioReservado.
        var (servicio, ordenes, outbox, _) = Construir();
        ordenes.Precargar(CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA));

        var resultado = await servicio.CancelarPorClienteAsync(
            OrdenId, SolicitudCancelacion(), CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, resultado.Orden.Estado);
        Assert.Empty(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        Assert.Single(outbox.PorTipo(OrdenCancelada.EventType));
    }
}
