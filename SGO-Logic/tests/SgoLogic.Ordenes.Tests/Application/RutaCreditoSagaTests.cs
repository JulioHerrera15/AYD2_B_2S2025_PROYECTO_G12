using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Ruta de credito a 30 dias de la saga: bifurcacion tras la reserva, respuesta de
/// Credito, reserva vencida, timeout, regla de identidad y cancelacion.
/// </summary>
public sealed class RutaCreditoSagaTests
{
    private const string OrdenId = "ORD-2026-000555";
    private const string ReservaId = "RSV-2026-000555";
    private const string Cliente = "cliente1@sgo.local";
    private static readonly DateTime Ahora = new(2026, 10, 2, 18, 0, 0, DateTimeKind.Utc);

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.CREDITO }
    };

    private static Orden CrearOrden(MedioPago medioPago = MedioPago.CREDITO) => new(
        OrdenId,
        "000555",
        Cliente,
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        medioPago,
        new[] { new LineaOrden("PROD-002", "Smart TV", 1, 900.00m) },
        96.43m);

    /// <summary>Orden a credito ya en EVALUACION_CREDITO_SOLICITADA, con la ultima transicion en <paramref name="evaluadaEn" />.</summary>
    private static Orden OrdenEnEvaluacion(DateTime? evaluadaEn = null)
    {
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA, cambiadoEn: Ahora.AddMinutes(-2));
        orden.RegistrarReserva(ReservaId, "CD-CENTRAL");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO, cambiadoEn: Ahora.AddMinutes(-1));
        orden.TransicionarA(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, cambiadoEn: evaluadaEn ?? Ahora.AddMinutes(-1));
        return orden;
    }

    private sealed class Escenario
    {
        public RepositorioOrdenesFalso Ordenes { get; } = new();
        public OutboxWriterFalso Outbox { get; } = new();
        public UnidadDeTrabajoFalsa Unidad { get; } = new();
        public RegistroMensajesProcesadosFalso Registro { get; } = new();

        public ServicioConfirmacionOrden Servicio(IUnidadDeTrabajo? unidad = null) => new(
            Ordenes,
            Outbox,
            unidad ?? Unidad,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso(),
            new CatalogoProductosFalso());

        public ManejadorEventosCredito ManejadorCredito() => new(
            Registro, Servicio(), Ordenes, Unidad, NullLogger<ManejadorEventosCredito>.Instance);

        public ManejadorEventosInventario ManejadorInventario() => new(
            Registro, Ordenes, Outbox, Unidad, NullLogger<ManejadorEventosInventario>.Instance);

        public ServicioTimeoutCredito Timeout(IUnidadDeTrabajo? unidad = null) => new(
            Ordenes,
            Servicio(unidad),
            Options.Create(new CreditoSagaOptions { TimeoutEvaluacionSegundos = 30 }),
            NullLogger<ServicioTimeoutCredito>.Instance,
            () => Ahora);
    }

    private static EventoInventarioRecibido Reservado() => new(
        "msg-reservado",
        "trx-1",
        InventarioReservado.EventType,
        OrdenId,
        new InventarioReservadoData(
            ReservaId, OrdenId, "CD-CENTRAL", TipoUbicacion.CENTRO_DISTRIBUCION, 900,
            new[] { new ReservaConfirmadaLineaData("PROD-002", 1) }, Ahora, Ahora.AddMinutes(15)));

    private static EventoCreditoRecibido Aprobado(string messageId = "msg-aprobado") =>
        new(messageId, "trx-1", CreditoAprobado.EventType, OrdenId, CargoId: "9f1c2d3e4b5a69788796a5b4c3d2e1f0");

    private const string MensajeRechazo =
        "Algunos de tus pagos recientes se hicieron después de la fecha acordada. Revisamos tu historial periódicamente.";

    private static EventoCreditoRecibido Rechazado(string messageId = "msg-rechazado") =>
        new(messageId, "trx-1", CreditoRechazado.EventType, OrdenId,
            CodigoMotivo: "PUNTAJE_INSUFICIENTE", MensajeCliente: MensajeRechazo);

    // ── Maquina de estados ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(EstadoOrden.INVENTARIO_RESERVADO, EstadoOrden.EVALUACION_CREDITO_SOLICITADA)]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.CREDITO_APROBADO)]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.RECHAZADA)]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.CANCELADA_RESERVA_VENCIDA)]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.CANCELADA)]
    [InlineData(EstadoOrden.CREDITO_APROBADO, EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL)]
    public void Transiciones_nuevas_validas(EstadoOrden desde, EstadoOrden hacia) =>
        Assert.True(MaquinaEstadosOrden.EsTransicionValida(desde, hacia));

    [Theory]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.COBRO_SOLICITADO)]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.PAGO_APLICADO)]
    [InlineData(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL)]
    [InlineData(EstadoOrden.CREDITO_APROBADO, EstadoOrden.CANCELADA)]
    [InlineData(EstadoOrden.CREDITO_APROBADO, EstadoOrden.RECHAZADA)]
    [InlineData(EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.EVALUACION_CREDITO_SOLICITADA)]
    [InlineData(EstadoOrden.COBRO_SOLICITADO, EstadoOrden.EVALUACION_CREDITO_SOLICITADA)]
    [InlineData(EstadoOrden.INVENTARIO_RESERVADO, EstadoOrden.CREDITO_APROBADO)]
    public void Transiciones_nuevas_invalidas(EstadoOrden desde, EstadoOrden hacia) =>
        Assert.False(MaquinaEstadosOrden.EsTransicionValida(desde, hacia));

    [Fact]
    public void Los_estados_nuevos_no_son_terminales()
    {
        Assert.False(MaquinaEstadosOrden.EsTerminal(EstadoOrden.EVALUACION_CREDITO_SOLICITADA));
        Assert.False(MaquinaEstadosOrden.EsTerminal(EstadoOrden.CREDITO_APROBADO));
    }

    // ── Bifurcacion tras la reserva ─────────────────────────────────────────────

    [Fact]
    public async Task Orden_a_credito_emite_EvaluacionCreditoSolicitada_y_nunca_CobroSolicitado()
    {
        var escenario = new Escenario();
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        escenario.Ordenes.Precargar(orden);

        var resultado = await escenario.ManejadorInventario().ManejarAsync(Reservado(), CancellationToken.None);

        Assert.Equal(ResultadoEventoInventario.Aplicado, resultado);
        Assert.Equal(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, orden.Estado);
        Assert.Empty(escenario.Outbox.PorTipo(CobroSolicitado.EventType));

        var evento = Assert.Single(escenario.Outbox.PorTipo(EvaluacionCreditoSolicitada.EventType));
        Assert.Equal(EvaluacionCreditoSolicitada.RoutingKey, evento.RoutingKey);
        var datos = Assert.IsType<EvaluacionCreditoSolicitadaData>(evento.Data);
        Assert.Equal(OrdenId, datos.OrdenId);
        Assert.Equal("000555", datos.NumeroOrden);
        Assert.Equal(Cliente, datos.ClienteId);
        Assert.Equal(orden.Total, datos.Monto);
        Assert.Equal(Moneda.GTQ, datos.Moneda);
        Assert.Equal(1, escenario.Unidad.Confirmaciones);
    }

    [Fact]
    public async Task Orden_con_tarjeta_sigue_a_COBRO_SOLICITADO_como_siempre()
    {
        var escenario = new Escenario();
        var orden = CrearOrden(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        escenario.Ordenes.Precargar(orden);

        await escenario.ManejadorInventario().ManejarAsync(Reservado(), CancellationToken.None);

        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, orden.Estado);
        Assert.Single(escenario.Outbox.PorTipo(CobroSolicitado.EventType));
        Assert.Empty(escenario.Outbox.PorTipo(EvaluacionCreditoSolicitada.EventType));
    }

    // ── Respuesta de Credito ────────────────────────────────────────────────────

    [Fact]
    public async Task CreditoAprobado_encadena_hasta_CONFIRMADA_PENDIENTE_FISCAL_y_pide_la_factura()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion();
        escenario.Ordenes.Precargar(orden);

        var resultado = await escenario.ManejadorCredito().ManejarAsync(Aprobado(), CancellationToken.None);

        Assert.Equal(ResultadoEventoCredito.Aplicado, resultado);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, orden.Estado);
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.CREDITO_APROBADO));
        Assert.Equal("CRE-9f1c2d3e4b5a69788796a5b4c3d2e1f0", orden.PagoId);
        Assert.Equal(1, escenario.Unidad.Confirmaciones);

        Assert.Single(escenario.Outbox.PorTipo(CertificacionFiscalSolicitada.EventType));
        var confirmada = Assert.IsType<OrdenConfirmadaData>(Assert.Single(escenario.Outbox.PorTipo(OrdenConfirmada.EventType)).Data);
        Assert.Equal("CRE-9f1c2d3e4b5a69788796a5b4c3d2e1f0", confirmada.PagoId);
        Assert.Equal(MedioPago.CREDITO, confirmada.MedioPago);
        Assert.Empty(escenario.Outbox.PorTipo(LiberacionReservaSolicitada.EventType));
        Assert.Empty(escenario.Outbox.PorTipo(CobroSolicitado.EventType));
    }

    [Fact]
    public async Task CreditoRechazado_termina_RECHAZADA_con_el_mensaje_y_libera_la_reserva()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion();
        escenario.Ordenes.Precargar(orden);

        await escenario.ManejadorCredito().ManejarAsync(Rechazado(), CancellationToken.None);

        Assert.Equal(EstadoOrden.RECHAZADA, orden.Estado);
        var transicion = orden.Transiciones.Last();
        Assert.Equal(MensajeRechazo, transicion.Motivo);
        Assert.True(transicion.EsCompensacion);

        var liberacion = Assert.IsType<LiberacionReservaSolicitadaData>(
            Assert.Single(escenario.Outbox.PorTipo(LiberacionReservaSolicitada.EventType)).Data);
        Assert.Equal(MotivoLiberacionReserva.CREDITO_RECHAZADO, liberacion.Motivo);
        Assert.Equal(ReservaId, liberacion.ReservaId);

        var cancelada = Assert.IsType<OrdenCanceladaData>(Assert.Single(escenario.Outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.Equal(MotivoCancelacion.CREDITO_RECHAZADO, cancelada.Motivo);
        Assert.Equal(MensajeRechazo, cancelada.Detalle);
        Assert.Equal(MedioPago.CREDITO, cancelada.MedioPago);
        Assert.False(cancelada.RequiereReembolso);
        Assert.Null(cancelada.MontoReembolso);
    }

    [Fact]
    public async Task El_mismo_evento_de_Credito_dos_veces_aplica_una_sola_vez()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion();
        escenario.Ordenes.Precargar(orden);
        var manejador = escenario.ManejadorCredito();

        var primera = await manejador.ManejarAsync(Aprobado("msg-dup"), CancellationToken.None);
        var segunda = await manejador.ManejarAsync(Aprobado("msg-dup"), CancellationToken.None);

        Assert.Equal(ResultadoEventoCredito.Aplicado, primera);
        Assert.Equal(ResultadoEventoCredito.Duplicado, segunda);
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.CREDITO_APROBADO));
        Assert.Single(escenario.Outbox.PorTipo(OrdenConfirmada.EventType));
        Assert.Single(escenario.Registro.Registrados);
    }

    [Fact]
    public async Task Aprobacion_tardia_sobre_orden_terminal_no_cambia_nada()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion();
        orden.TransicionarA(EstadoOrden.RECHAZADA, ServicioTimeoutCredito.MotivoVisible, esCompensacion: true);
        escenario.Ordenes.Precargar(orden);
        var transiciones = orden.Transiciones.Count;

        var resultado = await escenario.ManejadorCredito().ManejarAsync(Aprobado(), CancellationToken.None);

        Assert.Equal(ResultadoEventoCredito.Ignorado, resultado);
        Assert.Equal(EstadoOrden.RECHAZADA, orden.Estado);
        Assert.Equal(transiciones, orden.Transiciones.Count);
        Assert.Null(orden.PagoId);
        Assert.Empty(escenario.Outbox.Eventos);
        Assert.Single(escenario.Registro.Registrados);
    }

    // ── Reserva vencida y timeout ───────────────────────────────────────────────

    [Fact]
    public async Task Reserva_expirada_durante_la_evaluacion_cancela_por_reserva_vencida()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion();
        escenario.Ordenes.Precargar(orden);

        await escenario.ManejadorInventario().ManejarAsync(
            new EventoInventarioRecibido(
                "msg-expirada", "trx-1", ReservaInventarioExpirada.EventType, OrdenId,
                new ReservaInventarioExpiradaData(
                    ReservaId, OrdenId, "CD-CENTRAL", new[] { new ReservaConfirmadaLineaData("PROD-002", 1) }, Ahora, Ahora)),
            CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA_RESERVA_VENCIDA, orden.Estado);
        var cancelada = Assert.IsType<OrdenCanceladaData>(Assert.Single(escenario.Outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.Equal(MotivoCancelacion.RESERVA_VENCIDA, cancelada.Motivo);
        Assert.Equal(MedioPago.CREDITO, cancelada.MedioPago);
    }

    [Fact]
    public async Task Timeout_rechaza_la_evaluacion_vencida_y_libera_con_TIMEOUT_SAGA()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion(Ahora.AddSeconds(-31));
        escenario.Ordenes.Precargar(orden);

        var rechazadas = await escenario.Timeout().ProcesarVencidasAsync(CancellationToken.None);

        Assert.Equal(1, rechazadas);
        Assert.Equal(EstadoOrden.RECHAZADA, orden.Estado);
        Assert.Equal(ServicioTimeoutCredito.MotivoVisible, orden.Transiciones.Last().Motivo);

        var liberacion = Assert.IsType<LiberacionReservaSolicitadaData>(
            Assert.Single(escenario.Outbox.PorTipo(LiberacionReservaSolicitada.EventType)).Data);
        Assert.Equal(MotivoLiberacionReserva.TIMEOUT_SAGA, liberacion.Motivo);

        var cancelada = Assert.IsType<OrdenCanceladaData>(Assert.Single(escenario.Outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.Equal(MotivoCancelacion.CREDITO_SIN_RESPUESTA, cancelada.Motivo);
        Assert.Equal(MedioPago.CREDITO, cancelada.MedioPago);
    }

    [Fact]
    public async Task Timeout_no_toca_una_evaluacion_dentro_del_plazo()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion(Ahora.AddSeconds(-10));
        escenario.Ordenes.Precargar(orden);

        var rechazadas = await escenario.Timeout().ProcesarVencidasAsync(CancellationToken.None);

        Assert.Equal(0, rechazadas);
        Assert.Equal(EstadoOrden.EVALUACION_CREDITO_SOLICITADA, orden.Estado);
        Assert.Empty(escenario.Outbox.Eventos);
    }

    [Fact]
    public async Task Timeout_que_choca_con_una_aprobacion_concurrente_se_abandona_sin_error()
    {
        var escenario = new Escenario();
        escenario.Ordenes.Precargar(OrdenEnEvaluacion(Ahora.AddSeconds(-40)));
        var conflicto = new UnidadDeTrabajoConConflicto();

        var rechazadas = await escenario.Timeout(conflicto).ProcesarVencidasAsync(CancellationToken.None);

        Assert.Equal(0, rechazadas);
        Assert.Equal(1, conflicto.Intentos);
    }

    // ── Identidad y cancelacion ─────────────────────────────────────────────────

    private static ClaimsPrincipal Usuario(string? email) => email is null
        ? new ClaimsPrincipal(new ClaimsIdentity())
        : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }, "Bearer"));

    [Fact]
    public void Credito_sin_sesion_requiere_sesion_401() =>
        Assert.Equal(ResultadoIdentidadCredito.RequiereSesion, ReglaIdentidadCredito.Verificar("CREDITO", Usuario(null), Cliente));

    [Fact]
    public void Credito_con_token_de_otro_cliente_no_coincide_403() =>
        Assert.Equal(
            ResultadoIdentidadCredito.ClienteNoCoincide,
            ReglaIdentidadCredito.Verificar("credito", Usuario("cliente4@sgo.local"), Cliente));

    [Fact]
    public void Credito_con_el_mismo_email_sin_distinguir_mayusculas_se_permite() =>
        Assert.Equal(
            ResultadoIdentidadCredito.Permitida,
            ReglaIdentidadCredito.Verificar("CREDITO", Usuario("Cliente1@SGO.local"), Cliente));

    [Theory]
    [InlineData("TARJETA")]
    [InlineData("TRANSFERENCIA")]
    [InlineData(null)]
    public void Otros_medios_de_pago_no_exigen_sesion(string? medio) =>
        Assert.Equal(ResultadoIdentidadCredito.NoAplica, ReglaIdentidadCredito.Verificar(medio, Usuario(null), Cliente));

    [Fact]
    public async Task El_cliente_no_puede_cancelar_mientras_se_evalua_el_credito()
    {
        var escenario = new Escenario();
        escenario.Ordenes.Precargar(OrdenEnEvaluacion());

        var ex = await Assert.ThrowsAsync<ExcepcionCancelacionNoPermitida>(() =>
            escenario.Servicio().CancelarPorClienteAsync(OrdenId, new CancelarOrdenRequest(Cliente, null), CancellationToken.None));

        Assert.True(ex.PagoEnCurso);
    }

    [Fact]
    public async Task Cancelar_una_orden_a_credito_confirmada_no_pide_reembolso()
    {
        var escenario = new Escenario();
        var orden = OrdenEnEvaluacion();
        escenario.Ordenes.Precargar(orden);
        await escenario.ManejadorCredito().ManejarAsync(Aprobado(), CancellationToken.None);

        await escenario.Servicio().CancelarPorClienteAsync(OrdenId, new CancelarOrdenRequest(Cliente, null), CancellationToken.None);

        var cancelada = Assert.IsType<OrdenCanceladaData>(Assert.Single(escenario.Outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.Equal(MedioPago.CREDITO, cancelada.MedioPago);
        Assert.True(cancelada.EstabaConfirmada);
        Assert.False(cancelada.RequiereReembolso);
        Assert.Null(cancelada.MontoReembolso);
    }

    [Fact]
    public void PoliticaMediosPago_habilita_credito_sin_token()
    {
        var politica = new PoliticaMediosPago(MediosPago);

        Assert.True(politica.EstaHabilitado(Canal.WEB, MedioPago.CREDITO));
        Assert.False(politica.RequiereToken(MedioPago.CREDITO));
    }
}
