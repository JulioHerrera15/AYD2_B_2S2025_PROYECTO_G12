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
/// Cobranza (bloque 3): abonos, resultado del cobro, recordatorios, vencimiento con
/// pausa de la linea, politica v2 y su efecto en evaluacion y elegibilidad.
/// </summary>
public sealed class CobranzaTests
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

    /// <summary>cliente1 (EXCELENTE, 7500) con una compra a credito de <paramref name="monto" />.</summary>
    private async Task<CuentaPorCobrar> CompraAsync(decimal monto = 1000m, string ordenId = "ORD-2026-000301")
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

    private LineaCredito Linea => _almacen.Lineas.Single(l => l.ClienteId == Cliente);

    private PerfilCrediticio Perfil => _almacen.Perfiles.Single(p => p.ClienteId == Cliente);

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

    // ── Reglas de creacion ─────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_abono_valido_queda_SOLICITADO_y_pide_el_cobro_en_la_misma_transaccion()
    {
        var cuenta = await CompraAsync();
        var guardadosAntes = _almacen.Guardados;

        var abono = await AbonarAsync(cuenta, 400m);

        Assert.Equal("SOLICITADO", abono.Estado);
        Assert.True(abono.EnCurso);
        Assert.Equal("ABO-2026-000001", abono.Referencia);
        Assert.Equal(guardadosAntes + 1, _almacen.Guardados);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(CobroAbonoCreditoSolicitado.EventType, evento.EventType);
        Assert.Equal(CobroAbonoCreditoSolicitado.RoutingKey, evento.RoutingKey);
        var datos = Assert.IsType<CobroAbonoCreditoSolicitadoData>(evento.Data);
        Assert.Equal(abono.Id, datos.AbonoId);
        Assert.Equal(400m, datos.Monto);
        Assert.Equal(Token, datos.TokenMedioPago);
        Assert.Equal(1, datos.IntentoNumero);
    }

    [Fact]
    public async Task Una_cuenta_de_otro_cliente_se_trata_como_inexistente()
    {
        var cuenta = await CompraAsync();

        var resultado = await Abonos().CrearAsync("cliente2@sgo.local", cuenta.Id.ToString("N"), 100m, Token, CancellationToken.None);

        Assert.Equal(ErroresAbono.CuentaNoEncontrada, resultado.CodigoError);
        Assert.Empty(_almacen.Abonos);
        Assert.Empty(_outbox.Eventos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000.01)]
    [InlineData(9.99)]
    [InlineData(10.005)]
    public async Task Monto_invalido_responde_monto_invalido(double monto)
    {
        var cuenta = await CompraAsync();

        var resultado = await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), (decimal)monto, Token, CancellationToken.None);

        Assert.Equal(ErroresAbono.MontoInvalido, resultado.CodigoError);
        Assert.Contains("Q", resultado.MensajeError);
        Assert.Empty(_almacen.Abonos);
    }

    [Fact]
    public async Task Con_saldo_menor_al_minimo_solo_se_acepta_el_saldo_completo()
    {
        var cuenta = await CompraAsync(1000m);
        await CobradoAsync(await AbonarAsync(cuenta, 995m), 995m);

        var parcial = await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), 3m, Token, CancellationToken.None);
        var completo = await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), 5m, Token, CancellationToken.None);

        Assert.Equal(ErroresAbono.MontoInvalido, parcial.CodigoError);
        Assert.Contains("saldo completo", parcial.MensajeError);
        Assert.True(completo.Exito);
    }

    [Fact]
    public async Task Solo_un_abono_en_curso_por_cuenta()
    {
        var cuenta = await CompraAsync();
        await AbonarAsync(cuenta, 100m);

        var segundo = await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), 100m, Token, CancellationToken.None);

        Assert.Equal(ErroresAbono.AbonoEnCurso, segundo.CodigoError);
        Assert.Single(_almacen.Abonos);
    }

    [Fact]
    public async Task Dos_abonos_simultaneos_chocan_en_el_indice_unico_y_responden_abono_en_curso()
    {
        var cuenta = await CompraAsync();
        _almacen.DuplicadoEnSiguienteGuardado = true;

        var resultado = await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), 100m, Token, CancellationToken.None);

        Assert.Equal(ErroresAbono.AbonoEnCurso, resultado.CodigoError);
        Assert.Equal(1, _almacen.Descartes);
    }

    [Fact]
    public async Task Cuenta_pagada_o_anulada_no_es_pagable()
    {
        var pagada = await CompraAsync(500m, "ORD-2026-000302");
        await CobradoAsync(await AbonarAsync(pagada, 500m), 500m);
        var anulada = await CompraAsync(300m, "ORD-2026-000303");
        await new ServicioAnulacionCargos(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioAnulacionCargos>.Instance, Reloj)
            .ProcesarCancelacionAsync(Cancelada("ORD-2026-000303"), CancellationToken.None);

        var sobrePagada = await Abonos().CrearAsync(Cliente, pagada.Id.ToString("N"), 100m, Token, CancellationToken.None);
        var sobreAnulada = await Abonos().CrearAsync(Cliente, anulada.Id.ToString("N"), 100m, Token, CancellationToken.None);

        Assert.Equal(ErroresAbono.CuentaNoPagable, sobrePagada.CodigoError);
        Assert.Equal(ErroresAbono.CuentaNoPagable, sobreAnulada.CodigoError);
    }

    // ── Resultado del cobro ────────────────────────────────────────────────────

    [Fact]
    public async Task Cobrado_parcial_baja_el_saldo_de_la_cuenta_y_de_la_linea()
    {
        var cuenta = await CompraAsync(1000m);
        Assert.Equal(1000m, Linea.SaldoUtilizado);
        var abono = await AbonarAsync(cuenta, 400m);
        _outbox.Eventos.Clear();

        var resultado = await CobradoAsync(abono, 400m);

        Assert.Equal(ResultadoCobroAbono.Aplicado, resultado);
        Assert.Equal(600m, cuenta.SaldoPendiente);
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, cuenta.Estado);
        Assert.Equal(600m, Linea.SaldoUtilizado);
        Assert.Equal(6900m, Linea.Disponible);

        var aplicado = Assert.IsType<AbonoCreditoAplicadoData>(Assert.Single(_outbox.Eventos).Data);
        Assert.Equal(600m, aplicado.SaldoPendienteCuenta);
        Assert.Equal(6900m, aplicado.DisponibleLinea);

        var dto = (await Abonos().ObtenerAsync(Cliente, abono.Id, CancellationToken.None)).Abono!;
        Assert.Equal("APLICADO", dto.Estado);
        Assert.Equal("Recibimos tu pago", dto.Titulo);
        Assert.Contains("Q600.00", dto.Mensaje);
    }

    [Fact]
    public async Task Cobrado_total_a_tiempo_deja_la_cuenta_PAGADA_puntual()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await AbonarAsync(cuenta, 1000m);
        _outbox.Eventos.Clear();

        await CobradoAsync(abono, 1000m);

        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, cuenta.Estado);
        Assert.Equal(_ahora, cuenta.PagadaEn);
        Assert.True(cuenta.PagoPuntual);
        Assert.Equal(0m, Linea.SaldoUtilizado);
        Assert.Equal(7500m, Linea.Disponible);

        var pagada = Assert.IsType<CuentaCreditoPagadaData>(
            Assert.Single(_outbox.Eventos, e => e.EventType == CuentaCreditoPagada.EventType).Data);
        Assert.True(pagada.Puntual);

        var dto = (await Abonos().ObtenerAsync(Cliente, abono.Id, CancellationToken.None)).Abono!;
        Assert.Equal("Tu compra quedó pagada", dto.Titulo);
        Assert.Contains("disponible aumentó", dto.Mensaje);
    }

    [Fact]
    public async Task Cobrado_total_despues_del_vencimiento_cuenta_como_pago_tardio()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddDays(2);

        await CobradoAsync(await AbonarAsync(cuenta, 1000m), 1000m);

        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, cuenta.Estado);
        Assert.False(cuenta.PagoPuntual);
        // Un pago tardio propio entra a la puntualidad: 40 * 18 / 19.
        Assert.Equal(37.89m, Perfil.PuntosPuntualidad);
    }

    [Fact]
    public async Task Rechazado_marca_el_abono_y_no_cambia_nada_mas()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await AbonarAsync(cuenta, 400m);
        _outbox.Eventos.Clear();

        var resultado = await Resultados().RechazadoAsync(
            new AbonoCreditoRechazadoData(abono.Id, abono.Referencia, Cliente, "51", _ahora), CancellationToken.None);

        Assert.Equal(ResultadoCobroAbono.Rechazado, resultado);
        Assert.Equal(EstadoAbonoCredito.RECHAZADO, _almacen.Abonos.Single().Estado);
        Assert.Equal(1000m, cuenta.SaldoPendiente);
        Assert.Equal(1000m, Linea.SaldoUtilizado);
        Assert.Empty(_outbox.Eventos);

        var dto = (await Abonos().ObtenerAsync(Cliente, abono.Id, CancellationToken.None)).Abono!;
        Assert.Equal("Tu banco no autorizó el pago", dto.Titulo);
        Assert.Contains("ningún cargo", dto.Mensaje);
        Assert.False(dto.PuedeReintentar);
    }

    [Fact]
    public async Task Pendiente_permite_reintentar_con_el_mismo_abono_y_el_intento_siguiente()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await AbonarAsync(cuenta, 400m);
        await Resultados().PendienteAsync(
            new AbonoCreditoPendienteData(abono.Id, abono.Referencia, Cliente, "TIMEOUT", _ahora), CancellationToken.None);
        _outbox.Eventos.Clear();

        var pendiente = (await Abonos().ObtenerAsync(Cliente, abono.Id, CancellationToken.None)).Abono!;
        Assert.Equal("PENDIENTE", pendiente.Estado);
        Assert.True(pendiente.PuedeReintentar);
        Assert.Contains("No pagues de nuevo", pendiente.Mensaje);
        Assert.Equal(ErroresAbono.AbonoEnCurso,
            (await Abonos().CrearAsync(Cliente, cuenta.Id.ToString("N"), 100m, Token, CancellationToken.None)).CodigoError);

        var reintento = await Abonos().ReintentarAsync(Cliente, abono.Id, "tok_nuevo", CancellationToken.None);

        Assert.True(reintento.Exito);
        Assert.Equal(abono.Id, reintento.Abono!.Id);
        Assert.Single(_almacen.Abonos);
        var datos = Assert.IsType<CobroAbonoCreditoSolicitadoData>(Assert.Single(_outbox.Eventos).Data);
        Assert.Equal(abono.Id, datos.AbonoId);
        Assert.Equal(2, datos.IntentoNumero);
        Assert.Equal("tok_nuevo", datos.TokenMedioPago);

        await CobradoAsync(reintento.Abono, 400m);
        Assert.Equal(600m, cuenta.SaldoPendiente);
    }

    [Fact]
    public async Task Un_abono_que_no_esta_pendiente_no_se_puede_reintentar()
    {
        var cuenta = await CompraAsync();
        var abono = await AbonarAsync(cuenta, 100m);

        var resultado = await Abonos().ReintentarAsync(Cliente, abono.Id, "tok_nuevo", CancellationToken.None);

        Assert.Equal(ErroresAbono.AbonoNoReintentable, resultado.CodigoError);
    }

    [Fact]
    public async Task El_mismo_resultado_dos_veces_se_aplica_una_sola_vez()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await AbonarAsync(cuenta, 400m);

        var primero = await CobradoAsync(abono, 400m);
        var segundo = await CobradoAsync(abono, 400m);

        Assert.Equal(ResultadoCobroAbono.Aplicado, primero);
        Assert.Equal(ResultadoCobroAbono.Ignorado, segundo);
        Assert.Equal(600m, cuenta.SaldoPendiente);
        Assert.Equal(600m, Linea.SaldoUtilizado);
    }

    [Fact]
    public async Task Cobro_que_llega_con_la_cuenta_anulada_queda_pendiente_de_reembolso_sin_tocar_la_linea()
    {
        var cuenta = await CompraAsync(1000m);
        var abono = await AbonarAsync(cuenta, 400m);
        await new ServicioAnulacionCargos(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioAnulacionCargos>.Instance, Reloj)
            .ProcesarCancelacionAsync(Cancelada(cuenta.OrdenId), CancellationToken.None);
        Assert.Equal(0m, Linea.SaldoUtilizado);

        var resultado = await CobradoAsync(abono, 400m);

        Assert.Equal(ResultadoCobroAbono.ReembolsoPendiente, resultado);
        Assert.Equal(EstadoAbonoCredito.REEMBOLSO_PENDIENTE, _almacen.Abonos.Single().Estado);
        Assert.Equal(0m, Linea.SaldoUtilizado);
        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
    }

    [Fact]
    public async Task Anular_una_cuenta_con_abonos_aplicados_los_deja_por_reembolsar_y_libera_solo_el_saldo()
    {
        var cuenta = await CompraAsync(1000m);
        await CobradoAsync(await AbonarAsync(cuenta, 400m), 400m);
        Assert.Equal(600m, Linea.SaldoUtilizado);

        await new ServicioAnulacionCargos(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioAnulacionCargos>.Instance, Reloj)
            .ProcesarCancelacionAsync(Cancelada(cuenta.OrdenId), CancellationToken.None);

        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
        Assert.Equal(EstadoAbonoCredito.REEMBOLSO_PENDIENTE, _almacen.Abonos.Single().Estado);
        Assert.Equal(0m, Linea.SaldoUtilizado);
        var anulado = Assert.IsType<CargoCreditoAnuladoData>(
            Assert.Single(_outbox.Eventos, e => e.EventType == CargoCreditoAnulado.EventType).Data);
        Assert.Equal(600m, anulado.Monto);
    }

    // ── Recordatorios y vencimientos ───────────────────────────────────────────

    [Fact]
    public async Task El_recordatorio_se_emite_una_sola_vez()
    {
        var cuenta = await CompraAsync();
        cuenta.FijarVencimiento(_ahora.AddDays(2));

        var primero = await Vencimientos().ProcesarAsync(CancellationToken.None);
        var segundo = await Vencimientos().ProcesarAsync(CancellationToken.None);

        Assert.Equal(new ResultadoVencimientos(1, 0), primero);
        Assert.Equal(new ResultadoVencimientos(0, 0), segundo);
        var porVencer = Assert.IsType<CuentaCreditoPorVencerData>(
            Assert.Single(_outbox.Eventos, e => e.EventType == CuentaCreditoPorVencer.EventType).Data);
        Assert.Equal(2, porVencer.DiasRestantes);
        Assert.NotNull(cuenta.RecordatorioEnviadoEn);
    }

    [Fact]
    public async Task Una_cuenta_lejos_del_vencimiento_no_se_recuerda()
    {
        await CompraAsync();

        var resultado = await Vencimientos().ProcesarAsync(CancellationToken.None);

        Assert.Equal(new ResultadoVencimientos(0, 0), resultado);
    }

    [Fact]
    public async Task El_vencimiento_bloquea_la_linea_y_marca_DEUDA_VENCIDA_una_sola_vez()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddMinutes(1);

        var primero = await Vencimientos().ProcesarAsync(CancellationToken.None);
        var segundo = await Vencimientos().ProcesarAsync(CancellationToken.None);

        Assert.Equal(1, primero.Vencidas);
        Assert.Equal(0, segundo.Vencidas);
        Assert.Equal(EstadoCuentaPorCobrar.VENCIDA, cuenta.Estado);
        Assert.Equal(EstadoLineaCredito.BLOQUEADA, Linea.Estado);
        Assert.False(Perfil.Elegible);
        Assert.Equal(CodigosMotivoCredito.DeudaVencida, Perfil.CodigoMotivo);
        Assert.Single(_outbox.Eventos, e => e.EventType == CuentaCreditoVencida.EventType);
    }

    [Fact]
    public async Task Pagar_la_cuenta_vencida_reactiva_la_linea_y_el_perfil()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddDays(1);
        await Vencimientos().ProcesarAsync(CancellationToken.None);
        Assert.Equal(EstadoLineaCredito.BLOQUEADA, Linea.Estado);

        await CobradoAsync(await AbonarAsync(cuenta, 1000m), 1000m);

        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, cuenta.Estado);
        Assert.False(cuenta.PagoPuntual);
        Assert.Equal(EstadoLineaCredito.ACTIVA, Linea.Estado);
        Assert.True(Perfil.Elegible);
        Assert.Equal(7500m, Linea.Disponible);
    }

    // ── Politica v2 ────────────────────────────────────────────────────────────

    [Fact]
    public void Politica_v2_sin_comportamiento_propio_da_lo_mismo_que_v1()
    {
        var politica = DatosPrueba.Politica();
        foreach (var datos in new[] { DatosPrueba.Cliente1(), DatosPrueba.Cliente2(), DatosPrueba.Cliente3(), DatosPrueba.Cliente4(), DatosPrueba.Cliente5(), DatosPrueba.Cliente6() })
        {
            var historial = DatosPrueba.Entidad(datos);
            var sinComportamiento = politica.Evaluar(historial, DatosPrueba.Ahora);
            var conCeros = politica.Evaluar(historial, ComportamientoPropio.Ninguno, DatosPrueba.Ahora);

            Assert.Equal(sinComportamiento, conCeros);
        }

        Assert.Equal(98, politica.Evaluar(DatosPrueba.Entidad(DatosPrueba.Cliente1()), ComportamientoPropio.Ninguno, DatosPrueba.Ahora).Puntaje);
        Assert.Equal("2026.10-v2", politica.Opciones.VersionPolitica);
    }

    [Fact]
    public void Una_cuenta_vencida_sin_pagar_descarta_con_DEUDA_VENCIDA_justo_despues_de_SIN_HISTORIAL()
    {
        var politica = DatosPrueba.Politica();
        var conIncidente = DatosPrueba.Entidad(DatosPrueba.Cliente6());

        var evaluacion = politica.Evaluar(conIncidente, new ComportamientoPropio(0, 0, 1), DatosPrueba.Ahora);
        var sinHistorial = politica.Evaluar(null, new ComportamientoPropio(0, 0, 1), DatosPrueba.Ahora);

        Assert.Equal(CodigosMotivoCredito.DeudaVencida, evaluacion.CodigoMotivo);
        Assert.Equal(NivelCredito.NO_CALIFICA, evaluacion.Nivel);
        Assert.Equal(CodigosMotivoCredito.SinHistorial, sinHistorial.CodigoMotivo);
    }

    [Fact]
    public void Los_pagos_propios_suman_a_la_puntualidad_del_ERP()
    {
        var politica = DatosPrueba.Politica();
        var historial = DatosPrueba.Entidad(DatosPrueba.Cliente2()); // 8 puntuales, 1 tardio

        var tardios = politica.Evaluar(historial, new ComportamientoPropio(0, 3, 0), DatosPrueba.Ahora);
        var puntuales = politica.Evaluar(historial, new ComportamientoPropio(3, 0, 0), DatosPrueba.Ahora);

        Assert.Equal(40m * 8 / 12, tardios.Factores.Puntualidad);
        Assert.Equal(40m * 11 / 12, puntuales.Factores.Puntualidad);
        Assert.True(tardios.Puntaje < 79);
        Assert.True(puntuales.Puntaje > 79);
    }

    // ── Linea en pausa: evaluacion, elegibilidad y Mi credito ──────────────────

    [Fact]
    public async Task Con_la_linea_en_pausa_la_evaluacion_rechaza_con_LINEA_BLOQUEADA()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddDays(1);
        await Vencimientos().ProcesarAsync(CancellationToken.None);
        _outbox.Eventos.Clear();

        var resultado = await Evaluacion().EvaluarAsync(
            new EvaluacionCreditoSolicitadaData("ORD-2026-000399", "000399", Cliente, Canal.WEB, Moneda.GTQ, 100m, _ahora),
            CancellationToken.None);

        Assert.Equal(ResultadoEvaluacion.Rechazada, resultado);
        var rechazo = Assert.IsType<CreditoRechazadoData>(Assert.Single(_outbox.Eventos).Data);
        Assert.Equal(CodigosMotivoCredito.LineaBloqueada, rechazo.CodigoMotivo);
        Assert.Contains("vencida", rechazo.MensajeCliente);
    }

    [Fact]
    public async Task Con_la_linea_en_pausa_la_elegibilidad_responde_LINEA_BLOQUEADA_y_Mi_credito_avisa()
    {
        var cuenta = await CompraAsync(1000m);
        _ahora = cuenta.VenceEn.AddDays(1);
        await Vencimientos().ProcesarAsync(CancellationToken.None);
        var elegibilidad = new ServicioElegibilidad(_almacen, Options.Create(DatosPrueba.Opciones()), Reloj, Options.Create(new CobranzaOptions()));

        var oferta = await elegibilidad.ConsultarAsync(Cliente, 100m, CancellationToken.None);
        var credito = await elegibilidad.ObtenerMiCreditoAsync(Cliente, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.LineaBloqueada, oferta.Estado);
        Assert.False(oferta.Ofrecer);
        Assert.Equal("Tu crédito está en pausa", oferta.Titulo);

        Assert.Equal(1000m, credito.TotalPorPagar);
        Assert.Equal(cuenta.VenceEn, credito.ProximoVencimiento);
        Assert.Contains(credito.Avisos!, aviso => aviso.Tipo == "LINEA_EN_PAUSA");
        Assert.Contains(credito.Avisos!, aviso => aviso.Tipo == "VENCIDA" && aviso.Mensaje.Contains("Q1,000.00"));
        var compra = Assert.Single(credito.Compras);
        Assert.Equal("Vencida", compra.EtiquetaEstado);
        Assert.True(compra.PuedePagar);
    }

    [Fact]
    public async Task Mi_credito_muestra_vence_pronto_el_abono_en_curso_y_el_historial()
    {
        var cuenta = await CompraAsync(1000m);
        cuenta.FijarVencimiento(_ahora.AddDays(2));
        await CobradoAsync(await AbonarAsync(cuenta, 300m), 300m);
        await AbonarAsync(cuenta, 200m);
        var elegibilidad = new ServicioElegibilidad(_almacen, Options.Create(DatosPrueba.Opciones()), Reloj, Options.Create(new CobranzaOptions()));

        var credito = await elegibilidad.ObtenerMiCreditoAsync(Cliente, CancellationToken.None);

        var compra = Assert.Single(credito.Compras);
        Assert.Equal("Vence pronto", compra.EtiquetaEstado);
        Assert.False(compra.PuedePagar);
        Assert.NotNull(compra.AbonoEnCursoId);
        Assert.Equal(2, compra.Abonos!.Count);
        Assert.Contains(compra.Abonos, abono => abono.EtiquetaEstado == "Aplicado" && abono.Tarjeta == "Visa terminada en 4242");
        Assert.Contains(credito.Avisos!, aviso => aviso.Tipo == "POR_VENCER");
        Assert.Equal(700m, credito.TotalPorPagar);
    }

    [Fact]
    public async Task Los_textos_de_cobranza_no_tienen_codigos_ni_palabras_prohibidas()
    {
        var cuenta = await CompraAsync(1000m);
        var textos = new[]
        {
            CatalogoMensajesCredito.AbonoEnProceso(10m), CatalogoMensajesCredito.AbonoAplicadoParcial(10m, 5m),
            CatalogoMensajesCredito.AbonoAplicadoTotal(10m), CatalogoMensajesCredito.AbonoRechazado(),
            CatalogoMensajesCredito.AbonoPendiente(), CatalogoMensajesCredito.AbonoReembolsoPendiente(),
            CatalogoMensajesCredito.CompraPorVencer("000301", 10m, cuenta.VenceEn, 2),
            CatalogoMensajesCredito.CompraVencida("000301", 10m, cuenta.VenceEn), CatalogoMensajesCredito.LineaBloqueada()
        }.SelectMany(m => new[] { m.Titulo, m.Mensaje, m.Recomendacion })
         .Concat(Enum.GetValues<EstadoAbonoCredito>().Select(CatalogoMensajesCredito.EtiquetaEstadoAbono))
         .Append(CatalogoMensajesCredito.MontoInvalido(100m, 10m))
         .Append(CatalogoMensajesCredito.MontoInvalido(5m, 10m));

        foreach (var texto in textos)
        {
            Assert.DoesNotContain("puntaje", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fraude", texto, StringComparison.OrdinalIgnoreCase);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(texto, @"\b[A-Z]+_[A-Z_]+\b"), texto);
        }
    }

    private OrdenCanceladaData Cancelada(string ordenId) =>
        new(ordenId, ordenId[^6..], Cliente, null, true, MotivoCancelacion.CLIENTE, "Cancelada",
            false, null, null, null, null, _ahora, MedioPago.CREDITO);
}
