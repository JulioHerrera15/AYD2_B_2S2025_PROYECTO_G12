using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// Reembolsos que no se pierden (bloque 4): por cancelacion de una orden pagada con
/// tarjeta y de abonos a credito. Se registran PENDIENTE y el worker los lleva a PayKlik
/// con la misma clave de idempotencia en cada intento.
/// </summary>
public sealed class ReembolsosPendientesTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string Cliente = "cliente1@sgo.local";
    private const string ReferenciaAbono = "ABO-2026-000001";
    private const string AbonoId = "0f3c2a9d8e7b4c6a9b1d2e3f4a5b6c7d";

    private readonly string _baseDatos = $"pagos-reembolsos-{Guid.NewGuid()}";
    private readonly OutboxWriterFalso _outbox = new();
    private DateTime _ahora = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private PagosDbContext Contexto() =>
        new(new DbContextOptionsBuilder<PagosDbContext>().UseInMemoryDatabase(_baseDatos).Options);

    private ServicioReembolsosPendientes Servicio(PagosDbContext contexto, IClientePayKlik payklik, int maxIntentos = 8) => new(
        contexto,
        payklik,
        _outbox,
        Options.Create(new ReembolsosOptions { MaxIntentos = maxIntentos, BackoffBaseSegundos = 5, BackoffMaximoSegundos = 60 }),
        NullLogger<ServicioReembolsosPendientes>.Instance,
        () => _ahora);

    private async Task<Pago> SembrarPagoAsync(
        string ordenId = OrdenId,
        decimal monto = 1250m,
        ConceptoPago concepto = ConceptoPago.ORDEN,
        bool aplicado = true)
    {
        await using var contexto = Contexto();
        var pago = new Pago(ordenId, Cliente, Canal.WEB, Moneda.GTQ, monto, MedioPago.TARJETA, concepto: concepto);
        var intento = pago.RegistrarIntento(MedioPago.TARJETA, "tok_1", "4242", "VISA");
        if (aplicado)
        {
            intento.MarcarExitoso("PK-COBRO-1", "00", "Cargo aprobado", "4242", "VISA", 10, _ahora);
            pago.MarcarAplicado(_ahora);
        }
        else
        {
            intento.MarcarFallido("PROVEEDOR_5XX", "503", 10, _ahora);
            pago.MarcarPendiente(_ahora);
        }

        contexto.Pagos.Add(pago);
        contexto.Intentos.Add(intento);
        await contexto.SaveChangesAsync();
        return pago;
    }

    private static OrdenCanceladaData Cancelacion(bool requiereReembolso = true, MedioPago? medio = MedioPago.TARJETA) => new(
        OrdenId, "000777", Cliente, null, true, MotivoCancelacion.CLIENTE, "Cancelada por el cliente",
        requiereReembolso, "PK-COBRO-1", null, Moneda.GTQ, requiereReembolso ? 1250m : null, DateTime.UtcNow, medio);

    private static ReembolsoAbonoCreditoSolicitadoData SolicitudAbono(string reembolsoId, decimal monto) => new(
        reembolsoId, AbonoId, ReferenciaAbono, Cliente, "GTQ", monto, MotivosReembolsoAbono.Devolucion, DateTime.UtcNow);

    private async Task<ResultadoCreacionReembolso> CancelarAsync(OrdenCanceladaData datos)
    {
        await using var contexto = Contexto();
        var resultado = await Servicio(contexto, ClientePayKlikFalso.Aprueba()).CrearPorCancelacionAsync(datos, CancellationToken.None);
        await contexto.SaveChangesAsync();
        return resultado;
    }

    private async Task<ResultadoCreacionReembolso> SolicitarAbonoAsync(ReembolsoAbonoCreditoSolicitadoData datos)
    {
        await using var contexto = Contexto();
        var resultado = await Servicio(contexto, ClientePayKlikFalso.Aprueba()).CrearPorAbonoAsync(datos, CancellationToken.None);
        await contexto.SaveChangesAsync();
        return resultado;
    }

    private async Task<int> ProcesarAsync(IClientePayKlik payklik, int maxIntentos = 8)
    {
        await using var contexto = Contexto();
        return await Servicio(contexto, payklik, maxIntentos).ProcesarPendientesAsync(CancellationToken.None);
    }

    private async Task<List<Reembolso>> ReembolsosAsync()
    {
        await using var contexto = Contexto();
        return await contexto.Reembolsos.AsNoTracking().ToListAsync();
    }

    private static ClientePayKlikFalso PayKlikReembolso(Func<PeticionReembolsoPayKlik, Task<ResultadoPayKlik>> comportamiento)
    {
        var payklik = ClientePayKlikFalso.Aprueba();
        payklik.ComportamientoReembolso = comportamiento;
        return payklik;
    }

    private static Task<ResultadoPayKlik> Aprobado(string autorizacion = "PK-REEMB-1") =>
        Task.FromResult(new ResultadoPayKlik(autorizacion, EstadoRespuestaPayKlik.APROBADO, "00", "Reembolso aprobado", null, null, DateTime.UtcNow));

    // ── Cancelacion de orden pagada con tarjeta ────────────────────────────────

    [Fact]
    public async Task Cancelacion_con_pago_aplicado_crea_un_reembolso_pendiente_por_lo_cobrado()
    {
        var pago = await SembrarPagoAsync();

        var resultado = await CancelarAsync(Cancelacion());

        Assert.Equal(ResultadoCreacionReembolso.Creado, resultado);
        var reembolso = Assert.Single(await ReembolsosAsync());
        Assert.Equal(ConceptoReembolso.CANCELACION_ORDEN, reembolso.Concepto);
        Assert.Equal(OrdenId, reembolso.ReferenciaId);
        Assert.Equal(pago.Id, reembolso.PagoId);
        Assert.Equal(1250m, reembolso.Monto);
        Assert.Equal(EstadoReembolso.PENDIENTE, reembolso.Estado);
        Assert.Equal(0, reembolso.Intentos);
        Assert.Empty(_outbox.Eventos);
    }

    [Fact]
    public async Task Cancelacion_repetida_no_crea_un_segundo_reembolso()
    {
        await SembrarPagoAsync();

        await CancelarAsync(Cancelacion());
        var segunda = await CancelarAsync(Cancelacion());

        Assert.Equal(ResultadoCreacionReembolso.Duplicado, segunda);
        Assert.Single(await ReembolsosAsync());
    }

    [Fact]
    public async Task Cancelacion_a_credito_se_ignora()
    {
        await SembrarPagoAsync();

        var resultado = await CancelarAsync(Cancelacion(medio: MedioPago.CREDITO));

        Assert.Equal(ResultadoCreacionReembolso.Ignorado, resultado);
        Assert.Empty(await ReembolsosAsync());
    }

    [Fact]
    public async Task Cancelacion_que_no_requiere_reembolso_se_ignora()
    {
        await SembrarPagoAsync();

        var resultado = await CancelarAsync(Cancelacion(requiereReembolso: false));

        Assert.Equal(ResultadoCreacionReembolso.Ignorado, resultado);
        Assert.Empty(await ReembolsosAsync());
    }

    [Fact]
    public async Task Cancelacion_sin_pago_aplicado_se_ignora()
    {
        await SembrarPagoAsync(aplicado: false);

        var resultado = await CancelarAsync(Cancelacion());

        Assert.Equal(ResultadoCreacionReembolso.Ignorado, resultado);
        Assert.Empty(await ReembolsosAsync());
    }

    [Fact]
    public async Task Worker_aprobado_aplica_el_reembolso_con_su_id_como_clave_y_emite_el_evento()
    {
        await SembrarPagoAsync();
        await CancelarAsync(Cancelacion());
        var payklik = PayKlikReembolso(_ => Aprobado());

        var procesados = await ProcesarAsync(payklik);

        Assert.Equal(1, procesados);
        var reembolso = Assert.Single(await ReembolsosAsync());
        Assert.Equal(EstadoReembolso.APLICADO, reembolso.Estado);
        Assert.Equal("PK-REEMB-1", reembolso.AutorizacionId);
        Assert.Equal(1, reembolso.Intentos);
        Assert.Equal(reembolso.Id.ToString(), Assert.Single(payklik.ClavesReembolso));
        var peticion = Assert.Single(payklik.PeticionesReembolso);
        Assert.Equal("1250.00", peticion.Monto);
        Assert.Equal("PK-COBRO-1", peticion.AutorizacionOriginal);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(ReembolsoCancelacionAplicado.EventType, evento.EventType);
        Assert.Equal(ReembolsoCancelacionAplicado.RoutingKey, evento.RoutingKey);
        var datos = Assert.IsType<ReembolsoCancelacionAplicadoData>(evento.Data);
        Assert.Equal(OrdenId, datos.OrdenId);
        Assert.Equal(1250m, datos.Monto);
        Assert.Equal("PK-REEMB-1", datos.AutorizacionId);

        // Una pasada mas no vuelve a llamar al proveedor.
        _ahora = _ahora.AddMinutes(5);
        Assert.Equal(0, await ProcesarAsync(payklik));
        Assert.Single(payklik.ClavesReembolso);
    }

    [Fact]
    public async Task Falla_tecnica_reprograma_y_el_reintento_usa_la_misma_clave()
    {
        await SembrarPagoAsync();
        await CancelarAsync(Cancelacion());
        var llamadas = 0;
        var payklik = PayKlikReembolso(_ => ++llamadas == 1
            ? Task.FromException<ResultadoPayKlik>(new PayKlikNoDisponibleException("PayKlik respondio 503."))
            : Aprobado());

        await ProcesarAsync(payklik);

        var pendiente = Assert.Single(await ReembolsosAsync());
        Assert.Equal(EstadoReembolso.PENDIENTE, pendiente.Estado);
        Assert.Equal(1, pendiente.Intentos);
        Assert.Equal("PROVEEDOR_5XX", pendiente.CodigoRespuesta);
        Assert.Equal(_ahora.AddSeconds(5), pendiente.ProximoIntentoEn);
        Assert.Empty(_outbox.Eventos);

        // Antes del backoff no se reintenta.
        Assert.Equal(0, await ProcesarAsync(payklik));

        _ahora = _ahora.AddSeconds(5);
        await ProcesarAsync(payklik);

        var aplicado = Assert.Single(await ReembolsosAsync());
        Assert.Equal(EstadoReembolso.APLICADO, aplicado.Estado);
        Assert.Equal(2, aplicado.Intentos);
        Assert.Equal(2, payklik.ClavesReembolso.Count);
        Assert.All(payklik.ClavesReembolso, clave => Assert.Equal(aplicado.Id.ToString(), clave));
        Assert.Single(_outbox.PorTipo(ReembolsoCancelacionAplicado.EventType));
    }

    [Fact]
    public async Task Circuito_abierto_y_red_tambien_se_reprograman()
    {
        await SembrarPagoAsync();
        await CancelarAsync(Cancelacion());
        var llamadas = 0;
        var payklik = PayKlikReembolso(_ => ++llamadas == 1
            ? Task.FromException<ResultadoPayKlik>(new CircuitoAbiertoException("abierto"))
            : Task.FromException<ResultadoPayKlik>(new HttpRequestException("conexion rechazada")));

        await ProcesarAsync(payklik);
        _ahora = _ahora.AddSeconds(5);
        await ProcesarAsync(payklik);

        var reembolso = Assert.Single(await ReembolsosAsync());
        Assert.Equal(EstadoReembolso.PENDIENTE, reembolso.Estado);
        Assert.Equal(2, reembolso.Intentos);
        Assert.Equal("RED", reembolso.CodigoRespuesta);
        Assert.Equal(_ahora.AddSeconds(10), reembolso.ProximoIntentoEn);
    }

    [Fact]
    public async Task Rechazo_del_proveedor_es_definitivo_y_no_se_reintenta()
    {
        await SembrarPagoAsync();
        await CancelarAsync(Cancelacion());
        var payklik = PayKlikReembolso(_ => Task.FromResult(new ResultadoPayKlik(
            null, EstadoRespuestaPayKlik.RECHAZADO, "51", "Fondos insuficientes", null, null, DateTime.UtcNow)));

        await ProcesarAsync(payklik);
        _ahora = _ahora.AddHours(1);
        await ProcesarAsync(payklik);

        var reembolso = Assert.Single(await ReembolsosAsync());
        Assert.Equal(EstadoReembolso.FALLIDO, reembolso.Estado);
        Assert.Equal("51", reembolso.CodigoRespuesta);
        Assert.Null(reembolso.ProximoIntentoEn);
        Assert.Single(payklik.ClavesReembolso);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(ReembolsoCancelacionFallido.EventType, evento.EventType);
        Assert.Equal(ReembolsoCancelacionFallido.RoutingKey, evento.RoutingKey);
        Assert.Equal("Fondos insuficientes", Assert.IsType<ReembolsoCancelacionFallidoData>(evento.Data).MotivoFalla);
    }

    [Fact]
    public async Task Al_agotar_los_intentos_queda_fallido_con_un_solo_evento()
    {
        await SembrarPagoAsync();
        await CancelarAsync(Cancelacion());
        var payklik = PayKlikReembolso(_ =>
            Task.FromException<ResultadoPayKlik>(new TaskCanceledException("timeout")));

        for (var i = 0; i < 6; i++)
        {
            await ProcesarAsync(payklik, maxIntentos: 3);
            _ahora = _ahora.AddMinutes(2);
        }

        var reembolso = Assert.Single(await ReembolsosAsync());
        Assert.Equal(EstadoReembolso.FALLIDO, reembolso.Estado);
        Assert.Equal(3, reembolso.Intentos);
        Assert.Equal(3, payklik.ClavesReembolso.Count);
        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(ReembolsoCancelacionFallido.EventType, evento.EventType);
        Assert.Contains("TIMEOUT", Assert.IsType<ReembolsoCancelacionFallidoData>(evento.Data).MotivoFalla);
    }

    [Fact]
    public void El_backoff_se_duplica_hasta_el_tope()
    {
        var reembolso = new Reembolso(ConceptoReembolso.CANCELACION_ORDEN, OrdenId, Guid.NewGuid(), Cliente, 10m, Moneda.GTQ, _ahora);
        var esperas = new List<TimeSpan>();

        for (var i = 0; i < 6; i++)
        {
            reembolso.RegistrarFallaTecnica("RED", "red", _ahora, 8, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
            esperas.Add(reembolso.ProximoIntentoEn!.Value - _ahora);
        }

        Assert.Equal(new[] { 5d, 10d, 20d, 40d, 60d, 60d }, esperas.Select(espera => espera.TotalSeconds));
    }

    // ── Reembolso de abonos a credito ──────────────────────────────────────────

    [Fact]
    public async Task Reembolso_de_abono_no_supera_lo_cobrado_menos_lo_ya_reembolsado()
    {
        await SembrarPagoAsync(ReferenciaAbono, 400m, ConceptoPago.ABONO_CREDITO);

        var primero = await SolicitarAbonoAsync(SolicitudAbono("r1", 300m));
        var segundo = await SolicitarAbonoAsync(SolicitudAbono("r2", 300m));
        var tercero = await SolicitarAbonoAsync(SolicitudAbono("r3", 50m));
        var repetido = await SolicitarAbonoAsync(SolicitudAbono("r1", 300m));

        Assert.Equal(ResultadoCreacionReembolso.Creado, primero);
        Assert.Equal(ResultadoCreacionReembolso.Creado, segundo);
        Assert.Equal(ResultadoCreacionReembolso.Rechazado, tercero);
        Assert.Equal(ResultadoCreacionReembolso.Duplicado, repetido);

        var reembolsos = (await ReembolsosAsync()).OrderBy(r => r.ReferenciaId).ToList();
        Assert.Equal(new[] { "r1", "r2" }, reembolsos.Select(r => r.ReferenciaId));
        Assert.Equal(new[] { 300m, 100m }, reembolsos.Select(r => r.Monto));
        Assert.All(reembolsos, r => Assert.Equal(AbonoId, r.AbonoId));

        var fallo = Assert.Single(_outbox.Eventos);
        Assert.Equal(ReembolsoAbonoCreditoFallido.EventType, fallo.EventType);
        var datos = Assert.IsType<ReembolsoAbonoCreditoFallidoData>(fallo.Data);
        Assert.Equal("r3", datos.ReembolsoId);
        Assert.Equal(AbonoId, datos.AbonoId);
    }

    [Fact]
    public async Task Un_reembolso_de_abono_fallido_libera_su_monto_para_otra_solicitud()
    {
        await SembrarPagoAsync(ReferenciaAbono, 400m, ConceptoPago.ABONO_CREDITO);
        await SolicitarAbonoAsync(SolicitudAbono("r1", 400m));
        await ProcesarAsync(PayKlikReembolso(_ => Task.FromResult(new ResultadoPayKlik(
            null, EstadoRespuestaPayKlik.RECHAZADO, "51", "Rechazado", null, null, DateTime.UtcNow))));

        var otra = await SolicitarAbonoAsync(SolicitudAbono("r2", 400m));

        Assert.Equal(ResultadoCreacionReembolso.Creado, otra);
    }

    [Fact]
    public async Task Reembolso_de_abono_aplicado_emite_AbonoCreditoReembolsado()
    {
        await SembrarPagoAsync(ReferenciaAbono, 400m, ConceptoPago.ABONO_CREDITO);
        await SolicitarAbonoAsync(SolicitudAbono("r1", 150m));
        var payklik = PayKlikReembolso(_ => Aprobado("PK-REEMB-ABONO"));

        await ProcesarAsync(payklik);

        Assert.Equal(ReferenciaAbono, Assert.Single(payklik.PeticionesReembolso).Referencia);
        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(AbonoCreditoReembolsado.EventType, evento.EventType);
        Assert.Equal(AbonoCreditoReembolsado.RoutingKey, evento.RoutingKey);
        var datos = Assert.IsType<AbonoCreditoReembolsadoData>(evento.Data);
        Assert.Equal("r1", datos.ReembolsoId);
        Assert.Equal(AbonoId, datos.AbonoId);
        Assert.Equal(150m, datos.Monto);
        Assert.Equal("PK-REEMB-ABONO", datos.AutorizacionId);
    }

    [Fact]
    public async Task Reembolso_de_abono_sin_cobro_aplicado_informa_el_fallo()
    {
        var resultado = await SolicitarAbonoAsync(SolicitudAbono("r1", 100m));

        Assert.Equal(ResultadoCreacionReembolso.Rechazado, resultado);
        Assert.Empty(await ReembolsosAsync());
        Assert.Equal(ReembolsoAbonoCreditoFallido.EventType, Assert.Single(_outbox.Eventos).EventType);
    }

    // ── Devoluciones ───────────────────────────────────────────────────────────

    private static DevolucionRegistradaData Devolucion(MedioPago? medio) => new(
        "DEV-2026-000010", OrdenId, Cliente, TipoDevolucion.DEVOLUCION, false, "UBI-01",
        new[] { new DevolucionLineaData("SKU-1", 1, "No le gusto", null) },
        "GTQ", "1250.00", "PK-COBRO-1", string.Empty, "admin@sgo.com", DateTime.UtcNow, medio);

    [Fact]
    public async Task Devolucion_de_una_compra_a_credito_se_ignora_sin_llamar_al_proveedor()
    {
        var payklik = ClientePayKlikFalso.Aprueba();

        await using (var contexto = Contexto())
        {
            await new ServicioReembolso(contexto, payklik, _outbox, NullLogger<ServicioReembolso>.Instance)
                .ProcesarReembolsoAsync(Devolucion(MedioPago.CREDITO), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = Contexto();
        Assert.Empty(await verificacion.Pagos.ToListAsync());
        Assert.Empty(payklik.PeticionesReembolso);
        Assert.Empty(_outbox.Eventos);
    }

    [Fact]
    public async Task Devolucion_con_tarjeta_sigue_reembolsando_como_antes()
    {
        await SembrarPagoAsync();
        var payklik = ClientePayKlikFalso.Aprueba();

        await using (var contexto = Contexto())
        {
            await new ServicioReembolso(contexto, payklik, _outbox, NullLogger<ServicioReembolso>.Instance)
                .ProcesarReembolsoAsync(Devolucion(MedioPago.TARJETA), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Equal("DEV-2026-000010", Assert.Single(payklik.ClavesReembolso));
        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(ReembolsoAplicado.EventType, evento.EventType);
        Assert.Equal(1250m, Assert.IsType<ReembolsoAplicadoData>(evento.Data).Monto);
        Assert.Empty(await ReembolsosAsync());
    }
}
