using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Application;

/// <summary>
/// Evaluacion de compras a credito dentro de la saga, anulacion de cargos, reinicio
/// de demo y compras en "Mi credito".
/// </summary>
public sealed class EvaluacionCreditoTests
{
    private readonly AlmacenCreditoFalso _almacen = new();
    private readonly OutboxWriterFalso _outbox = new();

    private ServicioEvaluacionCredito Evaluacion() =>
        new(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioEvaluacionCredito>.Instance, () => DatosPrueba.Ahora);

    private ServicioAnulacionCargos Anulacion() =>
        new(_almacen, _outbox, DatosPrueba.Politica(), NullLogger<ServicioAnulacionCargos>.Instance, () => DatosPrueba.Ahora);

    private async Task SembrarAsync(params HistorialClienteErpActualizadoData[] historiales)
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, new OutboxWriterFalso());
        foreach (var historial in historiales)
        {
            await servicio.AplicarHistorialAsync(historial, CancellationToken.None);
        }
    }

    private static EvaluacionCreditoSolicitadaData Solicitud(string ordenId, string clienteId, decimal monto) =>
        new(ordenId, ordenId[^6..], clienteId, Canal.WEB, Moneda.GTQ, monto, DatosPrueba.Ahora);

    private static OrdenCanceladaData Cancelada(string ordenId, string clienteId, MedioPago? medio = MedioPago.CREDITO) =>
        new(ordenId, ordenId[^6..], clienteId, null, true, MotivoCancelacion.CLIENTE, "Cancelada por el cliente",
            false, null, null, null, null, DatosPrueba.Ahora, medio);

    private CreditoRechazadoData UnicoRechazo() =>
        Assert.IsType<CreditoRechazadoData>(Assert.Single(_outbox.Eventos, e => e.EventType == CreditoRechazado.EventType).Data);

    // ── Aprobacion ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aprobar_aparta_el_monto_emite_la_cuenta_y_encola_CreditoAprobado()
    {
        await SembrarAsync(DatosPrueba.Cliente1());
        var guardadosAntes = _almacen.Guardados;

        var resultado = await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000101", "Cliente1@sgo.local", 900m), CancellationToken.None);

        Assert.Equal(ResultadoEvaluacion.Aprobada, resultado);

        var linea = Assert.Single(_almacen.Lineas);
        Assert.Equal(900m, linea.SaldoUtilizado);
        Assert.Equal(6600m, linea.Disponible);

        var cuenta = Assert.Single(_almacen.Cuentas);
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, cuenta.Estado);
        Assert.Equal(900m, cuenta.MontoOriginal);
        Assert.Equal(900m, cuenta.SaldoPendiente);
        Assert.Equal(DatosPrueba.Ahora.AddDays(30), cuenta.VenceEn);
        Assert.Equal("000101", cuenta.NumeroOrden);
        Assert.Equal("cliente1@sgo.local", cuenta.ClienteId);

        var decision = Assert.Single(_almacen.Decisiones);
        Assert.Equal(ResultadoDecisionCredito.APROBADA, decision.Resultado);
        Assert.Equal(98, decision.Puntaje);
        Assert.Equal(NivelCredito.EXCELENTE, decision.Nivel);
        Assert.Equal(7500m, decision.DisponibleAntes);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(CreditoAprobado.EventType, evento.EventType);
        Assert.Equal(CreditoAprobado.RoutingKey, evento.RoutingKey);
        Assert.Equal("ORD-2026-000101", evento.AggregateId);
        var datos = Assert.IsType<CreditoAprobadoData>(evento.Data);
        Assert.Equal(cuenta.Id.ToString("N"), datos.CargoId);
        Assert.Equal(decision.Id.ToString("N"), datos.DecisionId);
        Assert.Equal(900m, datos.Monto);
        Assert.Equal("GTQ", datos.Moneda);
        Assert.Equal(cuenta.VenceEn, datos.FechaVencimiento);
        Assert.Equal(7500m, datos.LimiteAprobado);
        Assert.Equal(6600m, datos.DisponibleRestante);
        Assert.Equal(guardadosAntes + 1, _almacen.Guardados);
    }

    [Fact]
    public async Task Evaluar_dos_veces_la_misma_orden_es_idempotente()
    {
        await SembrarAsync(DatosPrueba.Cliente2());
        var servicio = Evaluacion();

        await servicio.EvaluarAsync(Solicitud("ORD-2026-000102", "cliente2@sgo.local", 1000m), CancellationToken.None);
        var segunda = await servicio.EvaluarAsync(Solicitud("ORD-2026-000102", "cliente2@sgo.local", 1000m), CancellationToken.None);

        Assert.Equal(ResultadoEvaluacion.YaDecidida, segunda);
        Assert.Single(_almacen.Decisiones);
        Assert.Single(_almacen.Cuentas);
        Assert.Single(_outbox.Eventos);
        Assert.Equal(1000m, Assert.Single(_almacen.Lineas).SaldoUtilizado);
    }

    [Fact]
    public async Task Dos_ordenes_que_juntas_superan_el_limite_solo_aprueban_una()
    {
        await SembrarAsync(DatosPrueba.Cliente3());
        var servicio = Evaluacion();

        var primera = await servicio.EvaluarAsync(Solicitud("ORD-2026-000103", "cliente3@sgo.local", 1000m), CancellationToken.None);
        var segunda = await servicio.EvaluarAsync(Solicitud("ORD-2026-000104", "cliente3@sgo.local", 800m), CancellationToken.None);

        Assert.Equal(ResultadoEvaluacion.Aprobada, primera);
        Assert.Equal(ResultadoEvaluacion.Rechazada, segunda);
        Assert.Equal(1000m, Assert.Single(_almacen.Lineas).SaldoUtilizado);
        Assert.Single(_almacen.Cuentas);
        Assert.Equal(CodigosMotivoCredito.CupoInsuficiente, UnicoRechazo().CodigoMotivo);
        Assert.Contains("Q500.00", UnicoRechazo().MensajeCliente);
    }

    // ── Rechazos ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_historial_rechaza_con_SIN_HISTORIAL()
    {
        var resultado = await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000110", "demo@sgo.local", 100m), CancellationToken.None);

        Assert.Equal(ResultadoEvaluacion.Rechazada, resultado);
        var rechazo = UnicoRechazo();
        Assert.Equal(CodigosMotivoCredito.SinHistorial, rechazo.CodigoMotivo);
        Assert.Contains("historial", rechazo.MensajeCliente);
        var decision = Assert.Single(_almacen.Decisiones);
        Assert.Equal(ResultadoDecisionCredito.RECHAZADA, decision.Resultado);
        Assert.Equal(0, decision.Puntaje);
        Assert.Empty(_almacen.Cuentas);
    }

    [Fact]
    public async Task Incidente_grave_rechaza_con_INCIDENTE_GRAVE()
    {
        await SembrarAsync(DatosPrueba.Cliente6());

        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000111", "cliente6@sgo.local", 100m), CancellationToken.None);

        var rechazo = UnicoRechazo();
        Assert.Equal(CodigosMotivoCredito.IncidenteGrave, rechazo.CodigoMotivo);
        Assert.Contains("atención al cliente", rechazo.MensajeCliente);
        Assert.Equal(95, Assert.Single(_almacen.Decisiones).Puntaje);
    }

    [Fact]
    public async Task Cuenta_reciente_rechaza_con_ANTIGUEDAD_INSUFICIENTE_y_los_dias_que_faltan()
    {
        await SembrarAsync(DatosPrueba.Cliente5());

        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000112", "cliente5@sgo.local", 100m), CancellationToken.None);

        var rechazo = UnicoRechazo();
        Assert.Equal(CodigosMotivoCredito.AntiguedadInsuficiente, rechazo.CodigoMotivo);
        Assert.Contains("Te faltan 50 días", rechazo.MensajeCliente);
    }

    [Fact]
    public async Task Devoluciones_altas_rechaza_con_DEVOLUCIONES_EXCESIVAS()
    {
        await SembrarAsync(DatosPrueba.Historial("devuelve@sgo.local", 400, 10, 12000m, 10, 0, 0, 4, 0, 0));

        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000113", "devuelve@sgo.local", 100m), CancellationToken.None);

        var rechazo = UnicoRechazo();
        Assert.Equal(CodigosMotivoCredito.DevolucionesExcesivas, rechazo.CodigoMotivo);
        Assert.Contains("devolución", rechazo.MensajeCliente);
    }

    [Fact]
    public async Task Puntaje_bajo_rechaza_con_PUNTAJE_INSUFICIENTE_y_habla_de_pagos_con_atraso()
    {
        await SembrarAsync(DatosPrueba.Cliente4());

        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000114", "cliente4@sgo.local", 100m), CancellationToken.None);

        var rechazo = UnicoRechazo();
        Assert.Equal(CodigosMotivoCredito.PuntajeInsuficiente, rechazo.CodigoMotivo);
        Assert.Contains("pagos", rechazo.MensajeCliente);
        Assert.Contains("fecha acordada", rechazo.MensajeCliente);
    }

    [Fact]
    public async Task Monto_mayor_al_disponible_rechaza_con_CUPO_INSUFICIENTE_sin_tocar_el_saldo()
    {
        await SembrarAsync(DatosPrueba.Cliente3());

        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000115", "cliente3@sgo.local", 2000m), CancellationToken.None);

        var rechazo = UnicoRechazo();
        Assert.Equal(CodigosMotivoCredito.CupoInsuficiente, rechazo.CodigoMotivo);
        Assert.Contains("Q1,500.00", rechazo.MensajeCliente);
        Assert.Contains("Q2,000.00", rechazo.MensajeCliente);
        Assert.Equal(0m, Assert.Single(_almacen.Lineas).SaldoUtilizado);
        Assert.Equal(1500m, Assert.Single(_almacen.Decisiones).DisponibleAntes);
    }

    [Fact]
    public async Task Los_mensajes_de_rechazo_no_tienen_codigos_ni_palabras_prohibidas()
    {
        await SembrarAsync(DatosPrueba.Cliente3(), DatosPrueba.Cliente4(), DatosPrueba.Cliente5(), DatosPrueba.Cliente6());
        var servicio = Evaluacion();
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000120", "cliente3@sgo.local", 5000m), CancellationToken.None);
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000121", "cliente4@sgo.local", 100m), CancellationToken.None);
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000122", "cliente5@sgo.local", 100m), CancellationToken.None);
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000123", "cliente6@sgo.local", 100m), CancellationToken.None);
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000124", "nadie@sgo.local", 100m), CancellationToken.None);

        var textos = _outbox.Eventos.Select(e => ((CreditoRechazadoData)e.Data).MensajeCliente)
            .Append(CatalogoMensajesCredito.MensajeRechazo(CatalogoMensajesCredito.OrdenCancelada()))
            .Concat(Enum.GetValues<EstadoCuentaPorCobrar>().Select(estado => CatalogoMensajesCredito.EtiquetaEstadoCompra(estado)))
            .ToList();

        Assert.Equal(5, _outbox.Eventos.Count);
        foreach (var texto in textos)
        {
            Assert.DoesNotContain("puntaje", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fraude", texto, StringComparison.OrdinalIgnoreCase);
            Assert.False(Regex.IsMatch(texto, @"\bERP\b", RegexOptions.IgnoreCase), texto);
            Assert.False(Regex.IsMatch(texto, @"\b[A-Z]+_[A-Z_]+\b"), texto);
        }
    }

    // ── Anulacion ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anular_el_cargo_devuelve_el_cupo_y_es_idempotente()
    {
        await SembrarAsync(DatosPrueba.Cliente2());
        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000130", "cliente2@sgo.local", 1200m), CancellationToken.None);

        var primera = await Anulacion().ProcesarCancelacionAsync(Cancelada("ORD-2026-000130", "cliente2@sgo.local"), CancellationToken.None);
        var segunda = await Anulacion().ProcesarCancelacionAsync(Cancelada("ORD-2026-000130", "cliente2@sgo.local"), CancellationToken.None);

        Assert.Equal(ResultadoAnulacion.Anulada, primera);
        Assert.Equal(ResultadoAnulacion.SinEfecto, segunda);

        var cuenta = Assert.Single(_almacen.Cuentas);
        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
        Assert.Equal(0m, cuenta.SaldoPendiente);
        Assert.Equal(DatosPrueba.Ahora, cuenta.AnuladaEn);
        Assert.Equal(0m, Assert.Single(_almacen.Lineas).SaldoUtilizado);

        var anulado = Assert.IsType<CargoCreditoAnuladoData>(
            Assert.Single(_outbox.Eventos, e => e.EventType == CargoCreditoAnulado.EventType).Data);
        Assert.Equal(1200m, anulado.Monto);
        Assert.Equal(cuenta.Id.ToString("N"), anulado.CargoId);
    }

    [Fact]
    public async Task Cancelacion_antes_de_la_evaluacion_hace_que_la_evaluacion_tardia_se_rechace_sin_cargo()
    {
        await SembrarAsync(DatosPrueba.Cliente1());

        var anulacion = await Anulacion().ProcesarCancelacionAsync(Cancelada("ORD-2026-000131", "cliente1@sgo.local"), CancellationToken.None);
        var evaluacion = await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000131", "cliente1@sgo.local", 500m), CancellationToken.None);

        Assert.Equal(ResultadoAnulacion.RegistradaSinCargo, anulacion);
        Assert.Equal(ResultadoEvaluacion.Rechazada, evaluacion);
        Assert.Equal(CodigosMotivoCredito.OrdenCancelada, UnicoRechazo().CodigoMotivo);
        Assert.Empty(_almacen.Cuentas);
        Assert.Equal(0m, Assert.Single(_almacen.Lineas).SaldoUtilizado);

        var decision = Assert.Single(_almacen.Decisiones);
        Assert.Equal(ResultadoDecisionCredito.RECHAZADA, decision.Resultado);
        Assert.Equal(500m, decision.Monto);
    }

    [Fact]
    public async Task Cancelacion_de_una_orden_que_no_es_a_credito_se_ignora()
    {
        var resultado = await Anulacion().ProcesarCancelacionAsync(
            Cancelada("ORD-2026-000132", "cliente1@sgo.local", MedioPago.TARJETA), CancellationToken.None);

        Assert.Equal(ResultadoAnulacion.Ignorada, resultado);
        Assert.Empty(_almacen.Decisiones);
        Assert.Empty(_outbox.Eventos);
    }

    [Fact]
    public async Task Cancelacion_de_una_orden_rechazada_no_tiene_efecto()
    {
        await SembrarAsync(DatosPrueba.Cliente4());
        await Evaluacion().EvaluarAsync(Solicitud("ORD-2026-000133", "cliente4@sgo.local", 100m), CancellationToken.None);

        var resultado = await Anulacion().ProcesarCancelacionAsync(Cancelada("ORD-2026-000133", "cliente4@sgo.local"), CancellationToken.None);

        Assert.Equal(ResultadoAnulacion.SinEfecto, resultado);
        Assert.Single(_almacen.Decisiones);
    }

    // ── Reinicio de demo y Mi credito ──────────────────────────────────────────

    [Fact]
    public async Task Reinicio_de_demo_borra_cuentas_y_decisiones_y_deja_el_saldo_en_cero()
    {
        await SembrarAsync(DatosPrueba.Cliente1(), DatosPrueba.Cliente2());
        var servicio = Evaluacion();
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000140", "cliente1@sgo.local", 900m), CancellationToken.None);
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000141", "cliente2@sgo.local", 700m), CancellationToken.None);

        var resultado = await new ServicioDemoCredito(_almacen, NullLogger<ServicioDemoCredito>.Instance, () => DatosPrueba.Ahora)
            .ReiniciarAsync(CancellationToken.None);

        Assert.Equal(new ResultadoReinicioDemo(2, 2, 2), resultado);
        Assert.Empty(_almacen.Cuentas);
        Assert.Empty(_almacen.Decisiones);
        Assert.All(_almacen.Lineas, linea => Assert.Equal(0m, linea.SaldoUtilizado));
        Assert.Equal(2, _almacen.Perfiles.Count);
        Assert.Equal(7500m, _almacen.Lineas.Single(l => l.ClienteId == "cliente1@sgo.local").LimiteAprobado);
    }

    [Fact]
    public async Task Mi_credito_muestra_saldo_real_y_compras_vigentes_primero()
    {
        await SembrarAsync(DatosPrueba.Cliente1());
        var servicio = Evaluacion();
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000150", "cliente1@sgo.local", 1000m), CancellationToken.None);
        await servicio.EvaluarAsync(Solicitud("ORD-2026-000151", "cliente1@sgo.local", 500m), CancellationToken.None);
        await Anulacion().ProcesarCancelacionAsync(Cancelada("ORD-2026-000150", "cliente1@sgo.local"), CancellationToken.None);

        var credito = await DatosPrueba.ServicioElegibilidad(_almacen).ObtenerMiCreditoAsync("cliente1@sgo.local", CancellationToken.None);

        Assert.Equal(500m, credito.SaldoUtilizado);
        Assert.Equal(7000m, credito.Disponible);
        Assert.Equal(new[] { "000151", "000150" }, credito.Compras.Select(c => c.NumeroOrden));

        var vigente = credito.Compras[0];
        Assert.Equal("VIGENTE", vigente.Estado);
        Assert.Equal("Por pagar", vigente.EtiquetaEstado);
        Assert.Equal(30, vigente.DiasRestantes);
        Assert.Equal(DatosPrueba.Ahora.AddDays(30), vigente.VenceEn);

        var anulada = credito.Compras[1];
        Assert.Equal("Anulada", anulada.EtiquetaEstado);
        Assert.Null(anulada.DiasRestantes);
    }
}
