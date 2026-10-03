using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Application;

/// <summary>
/// Reversos de dinero (bloque 4): devolucion de compras a credito y reembolso de abonos
/// (al anular la cuenta, al cobrar con la cuenta ya anulada, preexistentes y resultado).
/// </summary>
public sealed class ReembolsosCreditoTests
{
    private const string Cliente = "cliente1@sgo.local";
    private const string Token = "tok_prueba_1";

    private readonly AlmacenCreditoFalso _almacen = new();
    private readonly OutboxWriterFalso _outbox = new();
    private DateTime _ahora = DatosPrueba.Ahora;

    private DateTime Reloj() => _ahora;

    private ServicioPerfilCrediticio Perfiles() =>
        new(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioPerfilCrediticio>.Instance, Reloj);

    private ServicioAbonos Abonos() =>
        new(_almacen, _outbox, Options.Create(new CobranzaOptions()), NullLogger<ServicioAbonos>.Instance, Reloj);

    private ServicioResultadoAbonos Resultados() =>
        new(_almacen, _outbox, Perfiles(), NullLogger<ServicioResultadoAbonos>.Instance, Reloj);

    private ServicioVencimientos Vencimientos() =>
        new(_almacen, _outbox, Perfiles(), Options.Create(new CobranzaOptions()), NullLogger<ServicioVencimientos>.Instance, Reloj);

    private ServicioEvaluacionCredito Evaluacion() =>
        new(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioEvaluacionCredito>.Instance, Reloj);

    private ServicioDevolucionesCredito Devoluciones() =>
        new(_almacen, _outbox, Perfiles(), NullLogger<ServicioDevolucionesCredito>.Instance, Reloj);

    private ServicioReembolsosAbono Reembolsos() =>
        new(_almacen, _outbox, NullLogger<ServicioReembolsosAbono>.Instance, Reloj);

    private ServicioAnulacionCargos Anulacion() =>
        new(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioAnulacionCargos>.Instance, Reloj, Perfiles());

    private LineaCredito Linea => _almacen.Lineas.Single(l => l.ClienteId == Cliente);

    private async Task<CuentaPorCobrar> CompraAsync(decimal monto = 1000m, string ordenId = "ORD-2026-000401")
    {
        if (_almacen.Perfiles.Count == 0)
        {
            await Perfiles().AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None);
        }

        await Evaluacion().EvaluarAsync(
            new EvaluacionCreditoSolicitadaData(ordenId, ordenId[^6..], Cliente, Canal.WEB, Moneda.GTQ, monto, _ahora),
            CancellationToken.None);
        _outbox.Eventos.Clear();
        return _almacen.Cuentas.Single(c => c.OrdenId == ordenId);
    }

    private async Task<AbonoDto> AbonarAsync(CuentaPorCobrar cuenta, decimal monto)
    {
        var resultado = await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), monto, Token, CancellationToken.None);
        Assert.True(resultado.Exito, resultado.MensajeError);
        return resultado.Abono!;
    }

    private Task<ResultadoCobroAbono> CobradoAsync(AbonoDto abono, decimal monto) =>
        Resultados().CobradoAsync(
            new AbonoCreditoCobradoData(abono.Id, abono.Referencia, Cliente, "GTQ", monto, "PK-AUT-1", "4242", "VISA", _ahora),
            CancellationToken.None);

    private async Task<AbonoCredito> PagarAsync(CuentaPorCobrar cuenta, decimal monto)
    {
        var abono = await AbonarAsync(cuenta, monto);
        await CobradoAsync(abono, monto);
        return _almacen.Abonos.Single(a => a.Id.ToString("N") == abono.Id);
    }

    private static DevolucionRegistradaData Devolucion(
        string ordenId, decimal monto, bool esParcial, string devolucionId = "DEV-2026-000001", MedioPago? medio = MedioPago.CREDITO) =>
        new(devolucionId, ordenId, Cliente, TipoDevolucion.DEVOLUCION, esParcial, "UBI-01",
            new[] { new DevolucionLineaData("SKU-1", 1, "No le gusto", null) },
            "GTQ", monto.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), "CRE-1", "", "cliente", DatosPrueba.Ahora, medio);

    private OrdenCanceladaData Cancelada(string ordenId) =>
        new(ordenId, ordenId[^6..], Cliente, null, true, MotivoCancelacion.CLIENTE, "Cancelada",
            false, null, null, null, null, _ahora, MedioPago.CREDITO);

    private IReadOnlyList<ReembolsoAbonoCreditoSolicitadoData> Solicitudes() =>
        _outbox.PorTipo(ReembolsoAbonoCreditoSolicitado.EventType).Select(e => (ReembolsoAbonoCreditoSolicitadoData)e.Data).ToList();

    // ── Devolucion de compras a credito ────────────────────────────────────────

    [Fact]
    public async Task Devolucion_parcial_sin_pagos_baja_la_deuda_y_devuelve_el_cupo()
    {
        var cuenta = await CompraAsync(1000m);

        var resultado = await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 300m, esParcial: true), CancellationToken.None);

        Assert.Equal(ResultadoDevolucionCredito.Aplicada, resultado);
        Assert.Equal(700m, cuenta.SaldoPendiente);
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, cuenta.Estado);
        Assert.Equal(700m, Linea.SaldoUtilizado);

        var movimiento = Assert.Single(_almacen.Movimientos);
        Assert.Equal(TipoMovimientoCuenta.DEVOLUCION, movimiento.Tipo);
        Assert.Equal("DEV-2026-000001", movimiento.Referencia);
        Assert.Equal(300m, movimiento.Monto);
        Assert.Equal(300m, movimiento.MontoDescontadoDeuda);
        Assert.Equal(0m, movimiento.MontoAReembolsar);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(DevolucionCreditoAplicada.EventType, evento.EventType);
        Assert.Equal(DevolucionCreditoAplicada.RoutingKey, evento.RoutingKey);
        var datos = Assert.IsType<DevolucionCreditoAplicadaData>(evento.Data);
        Assert.Equal(cuenta.OrdenId, datos.OrdenOriginalId);
        Assert.Equal(300m, datos.MontoDescontadoDeuda);
        Assert.Equal(0m, datos.MontoAReembolsar);
    }

    [Fact]
    public async Task Devolucion_total_sin_pagos_anula_la_cuenta_por_devolucion()
    {
        var cuenta = await CompraAsync(1000m);

        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 1000m, esParcial: false), CancellationToken.None);

        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
        Assert.Equal(CuentaPorCobrar.MotivoDevolucion, cuenta.MotivoAnulacion);
        Assert.True(cuenta.AnuladaPorDevolucion);
        Assert.Equal(0m, cuenta.SaldoPendiente);
        Assert.Equal(0m, Linea.SaldoUtilizado);
        Assert.Empty(Solicitudes());

        var comportamiento = await Perfiles().ComportamientoAsync(Cliente, CancellationToken.None);
        Assert.Equal(0, comportamiento.PagadasATiempo);
        Assert.Equal(0, comportamiento.PagadasTarde);
    }

    [Fact]
    public async Task Devolucion_con_excedente_pagado_pide_reembolso_del_abono_mas_reciente_al_mas_antiguo()
    {
        var cuenta = await CompraAsync(1000m);
        var antiguo = await PagarAsync(cuenta, 300m);
        _ahora = _ahora.AddDays(1);
        var reciente = await PagarAsync(cuenta, 400m);
        _outbox.Eventos.Clear();
        _ahora = _ahora.AddDays(1);

        // Saldo 300; se devuelven 800 en forma parcial: 300 bajan la deuda y 500 ya
        // estaban pagados.
        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 800m, esParcial: true), CancellationToken.None);

        Assert.Equal(0m, cuenta.SaldoPendiente);
        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, cuenta.Estado);
        Assert.Null(cuenta.PagoPuntual);
        Assert.Equal(0m, Linea.SaldoUtilizado);

        var solicitudes = Solicitudes();
        Assert.Equal(2, solicitudes.Count);
        Assert.Equal(reciente.Id.ToString("N"), solicitudes[0].AbonoId);
        Assert.Equal(400m, solicitudes[0].Monto);
        Assert.Equal(antiguo.Id.ToString("N"), solicitudes[1].AbonoId);
        Assert.Equal(100m, solicitudes[1].Monto);
        Assert.All(solicitudes, s => Assert.Equal(MotivosReembolsoAbono.Devolucion, s.Motivo));
        Assert.All(solicitudes, s => Assert.False(string.IsNullOrWhiteSpace(s.ReembolsoId)));
        Assert.NotEqual(solicitudes[0].ReembolsoId, solicitudes[1].ReembolsoId);
        Assert.Equal(400m, reciente.MontoReembolsoSolicitado);
        Assert.Equal(100m, antiguo.MontoReembolsoSolicitado);

        var aplicada = Assert.IsType<DevolucionCreditoAplicadaData>(Assert.Single(_outbox.PorTipo(DevolucionCreditoAplicada.EventType)).Data);
        Assert.Equal(300m, aplicada.MontoDescontadoDeuda);
        Assert.Equal(500m, aplicada.MontoAReembolsar);

        // Cerrada por devolucion: no cuenta como pago puntual ni tardio.
        var comportamiento = await Perfiles().ComportamientoAsync(Cliente, CancellationToken.None);
        Assert.Equal(0, comportamiento.PagadasATiempo + comportamiento.PagadasTarde);
    }

    [Fact]
    public async Task Devolucion_de_una_compra_ya_pagada_pide_reembolso_de_todo_lo_devuelto()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await PagarAsync(cuenta, 1000m);
        Assert.True(cuenta.PagoPuntual);
        _outbox.Eventos.Clear();

        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 1000m, esParcial: false), CancellationToken.None);

        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, cuenta.Estado);
        Assert.True(cuenta.PagoPuntual);
        var solicitud = Assert.Single(Solicitudes());
        Assert.Equal(abono.Id.ToString("N"), solicitud.AbonoId);
        Assert.Equal(1000m, solicitud.Monto);
        Assert.Equal(abono.Referencia, solicitud.Referencia);
    }

    [Fact]
    public async Task La_misma_devolucion_dos_veces_se_aplica_una_sola_vez()
    {
        var cuenta = await CompraAsync(1000m);
        var datos = Devolucion(cuenta.OrdenId, 300m, esParcial: true);

        await Devoluciones().ProcesarAsync(datos, CancellationToken.None);
        var segunda = await Devoluciones().ProcesarAsync(datos, CancellationToken.None);

        Assert.Equal(ResultadoDevolucionCredito.YaAplicada, segunda);
        Assert.Equal(700m, cuenta.SaldoPendiente);
        Assert.Equal(700m, Linea.SaldoUtilizado);
        Assert.Single(_almacen.Movimientos);
        Assert.Single(_outbox.PorTipo(DevolucionCreditoAplicada.EventType));
    }

    [Fact]
    public async Task Devolucion_que_cierra_la_cuenta_vencida_desbloquea_la_linea()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddDays(1);
        await Vencimientos().ProcesarAsync(CancellationToken.None);
        Assert.Equal(EstadoCuentaPorCobrar.VENCIDA, cuenta.Estado);
        Assert.Equal(EstadoLineaCredito.BLOQUEADA, Linea.Estado);

        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 1000m, esParcial: false), CancellationToken.None);

        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
        Assert.Equal(EstadoLineaCredito.ACTIVA, Linea.Estado);
        Assert.Equal(0, (await Perfiles().ComportamientoAsync(Cliente, CancellationToken.None)).VencidasSinPagar);
    }

    [Fact]
    public async Task Devolucion_parcial_de_una_cuenta_vencida_la_deja_vencida_y_la_linea_en_pausa()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddDays(1);
        await Vencimientos().ProcesarAsync(CancellationToken.None);

        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 400m, esParcial: true), CancellationToken.None);

        Assert.Equal(EstadoCuentaPorCobrar.VENCIDA, cuenta.Estado);
        Assert.Equal(600m, cuenta.SaldoPendiente);
        Assert.Equal(EstadoLineaCredito.BLOQUEADA, Linea.Estado);
    }

    [Fact]
    public async Task Devolucion_de_una_compra_con_tarjeta_se_ignora()
    {
        var cuenta = await CompraAsync(1000m);

        var resultado = await Devoluciones().ProcesarAsync(
            Devolucion(cuenta.OrdenId, 300m, esParcial: true, medio: MedioPago.TARJETA), CancellationToken.None);

        Assert.Equal(ResultadoDevolucionCredito.Ignorada, resultado);
        Assert.Equal(1000m, cuenta.SaldoPendiente);
        Assert.Empty(_almacen.Movimientos);
        Assert.Empty(_outbox.Eventos);
    }

    // ── Abonos en REEMBOLSO_PENDIENTE ──────────────────────────────────────────

    [Fact]
    public async Task Anular_una_cuenta_con_abono_aplicado_pide_su_reembolso()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await PagarAsync(cuenta, 300m);
        _outbox.Eventos.Clear();

        await Anulacion().ProcesarCancelacionAsync(Cancelada(cuenta.OrdenId), CancellationToken.None);

        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
        Assert.Equal(EstadoAbonoCredito.REEMBOLSO_PENDIENTE, abono.Estado);
        Assert.Equal(_ahora, abono.ReembolsoSolicitadoEn);
        var solicitud = Assert.Single(Solicitudes());
        Assert.Equal(abono.Id.ToString("N"), solicitud.AbonoId);
        Assert.Equal(abono.Referencia, solicitud.Referencia);
        Assert.Equal(300m, solicitud.Monto);
        Assert.Equal(MotivosReembolsoAbono.Cancelacion, solicitud.Motivo);
        Assert.Equal(ReembolsoAbonoCreditoSolicitado.RoutingKey,
            _outbox.PorTipo(ReembolsoAbonoCreditoSolicitado.EventType).Single().RoutingKey);
    }

    [Fact]
    public async Task Un_cobro_que_llega_con_la_cuenta_anulada_pide_su_reembolso()
    {
        var cuenta = await CompraAsync(1000m);
        var abonoDto = await AbonarAsync(cuenta, 250m);
        await Anulacion().ProcesarCancelacionAsync(Cancelada(cuenta.OrdenId), CancellationToken.None);
        _outbox.Eventos.Clear();

        var resultado = await CobradoAsync(abonoDto, 250m);

        Assert.Equal(ResultadoCobroAbono.ReembolsoPendiente, resultado);
        var abono = _almacen.Abonos.Single();
        Assert.Equal(EstadoAbonoCredito.REEMBOLSO_PENDIENTE, abono.Estado);
        var solicitud = Assert.Single(Solicitudes());
        Assert.Equal(250m, solicitud.Monto);
        Assert.Equal(MotivosReembolsoAbono.Cancelacion, solicitud.Motivo);
    }

    [Fact]
    public async Task Los_abonos_pendientes_de_reembolso_sin_solicitud_la_piden_una_vez()
    {
        var cuenta = await CompraAsync(1000m);
        var abonoDto = await AbonarAsync(cuenta, 200m);
        var abono = _almacen.Abonos.Single();
        // Como quedaron antes de este bloque: REEMBOLSO_PENDIENTE sin solicitud.
        abono.MarcarReembolsoPendiente("PK-AUT-9", "4242", "VISA", _ahora);
        _outbox.Eventos.Clear();

        var primero = await Reembolsos().SolicitarPendientesAsync(CancellationToken.None);
        var segundo = await Reembolsos().SolicitarPendientesAsync(CancellationToken.None);

        Assert.Equal(1, primero);
        Assert.Equal(0, segundo);
        var solicitud = Assert.Single(Solicitudes());
        Assert.Equal(abonoDto.Id, solicitud.AbonoId);
        Assert.Equal(200m, solicitud.Monto);
        Assert.NotNull(abono.ReembolsoSolicitadoEn);
    }

    // ── Resultado del reembolso ────────────────────────────────────────────────

    [Fact]
    public async Task Reembolso_aplicado_completo_deja_el_abono_REEMBOLSADO_una_sola_vez()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await PagarAsync(cuenta, 300m);
        await Anulacion().ProcesarCancelacionAsync(Cancelada(cuenta.OrdenId), CancellationToken.None);
        var solicitud = Assert.Single(Solicitudes());
        var datos = new AbonoCreditoReembolsadoData(
            solicitud.ReembolsoId, solicitud.AbonoId, Cliente, "GTQ", 300m, "PK-REEMB-1", _ahora);

        var primero = await Reembolsos().ReembolsadoAsync(datos, CancellationToken.None);
        var repetido = await Reembolsos().ReembolsadoAsync(datos, CancellationToken.None);

        Assert.Equal(ResultadoReembolsoAbono.Aplicado, primero);
        Assert.Equal(ResultadoReembolsoAbono.Ignorado, repetido);
        Assert.Equal(EstadoAbonoCredito.REEMBOLSADO, abono.Estado);
        Assert.Equal(300m, abono.MontoReembolsado);
        var movimiento = Assert.Single(_almacen.Movimientos, m => m.Tipo == TipoMovimientoCuenta.REEMBOLSO_ABONO);
        Assert.Equal(solicitud.ReembolsoId, movimiento.Referencia);
        Assert.Equal(CatalogoMensajesCredito.ReembolsoAplicado, CatalogoMensajesCredito.EstadoReembolsoAbono(abono));
    }

    [Fact]
    public async Task Reembolso_parcial_suma_lo_reembolsado_sin_cerrar_el_abono()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await PagarAsync(cuenta, 1000m);
        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 400m, esParcial: true), CancellationToken.None);
        var solicitud = Assert.Single(Solicitudes());
        Assert.Equal(CatalogoMensajesCredito.ReembolsoEnProceso, CatalogoMensajesCredito.EstadoReembolsoAbono(abono));

        await Reembolsos().ReembolsadoAsync(
            new AbonoCreditoReembolsadoData(solicitud.ReembolsoId, solicitud.AbonoId, Cliente, "GTQ", 400m, "PK-R", _ahora),
            CancellationToken.None);

        Assert.Equal(EstadoAbonoCredito.APLICADO, abono.Estado);
        Assert.Equal(400m, abono.MontoReembolsado);
        Assert.Equal(CatalogoMensajesCredito.ReembolsoAplicado, CatalogoMensajesCredito.EstadoReembolsoAbono(abono));
    }

    [Fact]
    public async Task Reembolso_fallido_deja_el_abono_para_atencion_manual()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await PagarAsync(cuenta, 300m);
        await Anulacion().ProcesarCancelacionAsync(Cancelada(cuenta.OrdenId), CancellationToken.None);
        var solicitud = Assert.Single(Solicitudes());

        var resultado = await Reembolsos().FallidoAsync(
            new ReembolsoAbonoCreditoFallidoData(solicitud.ReembolsoId, solicitud.AbonoId, Cliente, "GTQ", 300m, "Fondos insuficientes", _ahora),
            CancellationToken.None);

        Assert.Equal(ResultadoReembolsoAbono.Fallido, resultado);
        Assert.Equal(EstadoAbonoCredito.REEMBOLSO_PENDIENTE, abono.Estado);
        Assert.Equal(_ahora, abono.ReembolsoFallidoEn);
        Assert.Equal("Fondos insuficientes", abono.MotivoFallaReembolso);
        Assert.Equal(CatalogoMensajesCredito.ReembolsoFallido, CatalogoMensajesCredito.EstadoReembolsoAbono(abono));
        // No se vuelve a pedir solo: queda para atencion manual.
        Assert.Equal(0, await Reembolsos().SolicitarPendientesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Resultado_de_un_abono_inexistente_se_ignora()
    {
        var resultado = await Reembolsos().ReembolsadoAsync(
            new AbonoCreditoReembolsadoData("r1", Guid.NewGuid().ToString("N"), Cliente, "GTQ", 10m, "PK", _ahora),
            CancellationToken.None);

        Assert.Equal(ResultadoReembolsoAbono.Ignorado, resultado);
        Assert.Empty(_almacen.Movimientos);
    }

    // ── Mi credito ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mi_credito_muestra_las_devoluciones_y_el_estado_del_reembolso_de_cada_pago()
    {
        var cuenta = await CompraAsync(1000m);
        await PagarAsync(cuenta, 600m);
        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 700m, esParcial: true), CancellationToken.None);
        var elegibilidad = new ServicioElegibilidad(_almacen, Options.Create(DatosPrueba.Opciones()), Reloj, Options.Create(new CobranzaOptions()));

        var credito = await elegibilidad.ObtenerMiCreditoAsync(Cliente, CancellationToken.None);

        var compra = Assert.Single(credito.Compras);
        Assert.Equal(0m, compra.SaldoPendiente);
        Assert.Equal(0m, credito.TotalPorPagar);
        var movimiento = Assert.Single(compra.Movimientos!);
        Assert.Equal("DEVOLUCION", movimiento.Tipo);
        Assert.Equal(400m, movimiento.MontoDescontadoDeuda);
        Assert.Equal(300m, movimiento.MontoAReembolsar);
        Assert.Contains("Q700.00", movimiento.Descripcion);
        Assert.Contains("Q300.00", movimiento.Descripcion);
        var pago = Assert.Single(compra.Abonos!);
        Assert.Equal("Reembolso a tu tarjeta en proceso", pago.EstadoReembolso);
    }

    [Fact]
    public async Task Mi_credito_muestra_Devuelta_en_una_compra_anulada_por_devolucion()
    {
        var cuenta = await CompraAsync(1000m);
        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 1000m, esParcial: false), CancellationToken.None);
        var elegibilidad = new ServicioElegibilidad(_almacen, Options.Create(DatosPrueba.Opciones()), Reloj, Options.Create(new CobranzaOptions()));

        var credito = await elegibilidad.ObtenerMiCreditoAsync(Cliente, CancellationToken.None);

        Assert.Equal("Devuelta", Assert.Single(credito.Compras).EtiquetaEstado);
        Assert.Equal(0m, credito.SaldoUtilizado);
    }

    [Fact]
    public async Task El_reinicio_de_demo_borra_tambien_los_movimientos()
    {
        var cuenta = await CompraAsync(1000m);
        await Devoluciones().ProcesarAsync(Devolucion(cuenta.OrdenId, 300m, esParcial: true), CancellationToken.None);

        var resultado = await new ServicioDemoCredito(_almacen, NullLogger<ServicioDemoCredito>.Instance, Reloj, Perfiles())
            .ReiniciarAsync(CancellationToken.None);

        Assert.Equal(1, resultado.MovimientosBorrados);
        Assert.Empty(_almacen.Movimientos);
    }
}
