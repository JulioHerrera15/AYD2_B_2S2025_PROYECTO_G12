using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

public sealed class ServicioCobroTests
{
    private const string OrdenId = "ORD-2026-000001";

    private static SolicitudCobro Solicitud(string ordenId = OrdenId) => new(
        ordenId,
        "CLI-000045",
        Canal.WEB,
        Moneda.GTQ,
        1400.00m,
        MedioPago.TARJETA,
        TokenMedioPago: "tok_pk_9f2a7c41d8",
        UltimosCuatro: "4417",
        Marca: "VISA");

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"pagos-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public PagosDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<PagosDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        public ServicioCobro Servicio(
            PagosDbContext contexto,
            IClientePayKlik payklik,
            PayKlikOptions? payklikOpciones = null) => new(
            contexto,
            payklik,
            Outbox,
            // Por defecto sin reintentos: cada prueba existente valida un unico
            // camino de llamada. Las pruebas de reintento pasan sus propias opciones.
            Options.Create(payklikOpciones ?? new PayKlikOptions { MaxReintentos = 0, BackoffBaseMs = 0 }),
            Options.Create(new ReintentoOptions { ExpiracionMinutos = 60 }),
            NullLogger<ServicioCobro>.Instance);
    }

    [Fact]
    public async Task El_intento_se_registra_en_INICIADO_antes_de_invocar_al_proveedor()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();
        var payklik = ClientePayKlikFalso.Aprueba();

        var intentosAlInvocar = -1;
        payklik.AntesDeResponder = () => intentosAlInvocar = contexto.ChangeTracker
            .Entries<IntentoPago>()
            .Count(entrada => entrada.Entity.Estado == EstadoIntentoPago.INICIADO);

        await escenario.Servicio(contexto, payklik).ProcesarCobroAsync(Solicitud(), CancellationToken.None);

        Assert.Equal(1, intentosAlInvocar);
        Assert.Single(payklik.Peticiones);
    }

    [Fact]
    public async Task Aprobado_deja_el_pago_APLICADO_y_encola_PagoAplicado()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Aprueba("PK-AUTH-77"))
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.Equal(1, pago.Secuencia);
        var intento = Assert.Single(pago.Intentos);
        Assert.Equal(EstadoIntentoPago.EXITOSO, intento.Estado);
        Assert.Equal("PK-AUTH-77", intento.AutorizacionId);

        var evento = Assert.Single(escenario.Outbox.PorTipo(PagoAplicado.EventType));
        Assert.Equal(PagoAplicado.RoutingKey, evento.RoutingKey);
        Assert.Equal(OrdenId, evento.AggregateId);
        Assert.Equal(1, evento.Sequence);
        var data = Assert.IsType<PagoAplicadoData>(evento.Data);
        Assert.Equal(EstadoPago.APLICADO, data.Estado);
        Assert.Equal("PK-AUTH-77", data.AutorizacionId);
        Assert.Equal(1, data.IntentoNumero);
    }

    [Fact]
    public async Task Rechazado_deja_el_pago_RECHAZADO_y_encola_PagoRechazado()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Rechaza())
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Equal(EstadoIntentoPago.RECHAZADO, Assert.Single(pago.Intentos).Estado);
        Assert.Empty(await verificacion.EnlacesReintento.ToListAsync());

        var evento = Assert.Single(escenario.Outbox.PorTipo(PagoRechazado.EventType));
        Assert.Equal(PagoRechazado.RoutingKey, evento.RoutingKey);
        Assert.IsType<PagoRechazadoData>(evento.Data);
    }

    [Fact]
    public async Task Timeout_deja_el_pago_PENDIENTE_y_genera_un_enlace_de_reintento()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Timeout())
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        Assert.Equal(EstadoPago.PENDIENTE, pago.Estado);
        var intento = Assert.Single(pago.Intentos);
        Assert.Equal(EstadoIntentoPago.FALLIDO, intento.Estado);
        Assert.Equal("TIMEOUT", intento.CodigoRespuesta);

        var enlace = await verificacion.EnlacesReintento.SingleAsync();
        Assert.False(enlace.Usado);
        Assert.Equal(OrdenId, enlace.OrdenId);
        Assert.True(enlace.ExpiraEn > DateTime.UtcNow);

        var evento = Assert.Single(escenario.Outbox.PorTipo(PagoPendiente.EventType));
        var data = Assert.IsType<PagoPendienteData>(evento.Data);
        Assert.Equal(EstadoPago.PENDIENTE, data.Estado);
        Assert.Equal(enlace.Token, data.EnlaceReintento.Token);
        Assert.Contains(enlace.Token, data.EnlaceReintento.Url);
        Assert.NotEmpty(data.MotivoFalla);
    }

    [Fact]
    public async Task El_proveedor_no_disponible_tambien_deja_el_pago_PENDIENTE()
    {
        var escenario = new Escenario();

        await using var contexto = escenario.NuevoContexto();
        await escenario.Servicio(contexto, ClientePayKlikFalso.NoDisponible())
            .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
        await contexto.SaveChangesAsync();

        var pago = await contexto.Pagos.Include(p => p.Intentos).SingleAsync();
        Assert.Equal(EstadoPago.PENDIENTE, pago.Estado);
        Assert.Equal("PROVEEDOR_5XX", Assert.Single(pago.Intentos).CodigoRespuesta);
        Assert.Single(escenario.Outbox.PorTipo(PagoPendiente.EventType));
    }

    [Fact]
    public async Task El_reintento_incrementa_el_numero_de_intento()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Timeout())
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Aprueba())
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        Assert.Equal(2, pago.Intentos.Count);
        Assert.Equal(new[] { 1, 2 }, pago.Intentos.OrderBy(i => i.IntentoNumero).Select(i => i.IntentoNumero));
        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.Equal(2, pago.Secuencia);

        var aplicado = Assert.Single(escenario.Outbox.PorTipo(PagoAplicado.EventType));
        Assert.Equal(2, Assert.IsType<PagoAplicadoData>(aplicado.Data).IntentoNumero);
    }

    [Fact]
    public async Task Con_el_circuito_abierto_no_se_invoca_al_proveedor_pero_se_registra_el_intento_CIRCUITO_ABIERTO()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.CircuitoAbierto();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        // No se invoco al proveedor.
        Assert.Empty(payklik.Peticiones);

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        // Igual se registro el intento, en FALLIDO y con motivo CIRCUITO_ABIERTO.
        Assert.Equal(EstadoPago.PENDIENTE, pago.Estado);
        var intento = Assert.Single(pago.Intentos);
        Assert.Equal(EstadoIntentoPago.FALLIDO, intento.Estado);
        Assert.Equal("CIRCUITO_ABIERTO", intento.CodigoRespuesta);
        Assert.True(intento.LatenciaMs < 100, "la latencia con el circuito abierto debe ser cercana a cero");

        // El pago queda PENDIENTE con enlace de reintento y se publica PagoPendiente.
        var enlace = await verificacion.EnlacesReintento.SingleAsync();
        Assert.Equal(OrdenId, enlace.OrdenId);
        var evento = Assert.Single(escenario.Outbox.PorTipo(PagoPendiente.EventType));
        var data = Assert.IsType<PagoPendienteData>(evento.Data);
        Assert.Equal("CIRCUITO_ABIERTO", data.CodigoRespuesta);
    }

    [Fact]
    public async Task Un_rechazo_de_negocio_no_dispara_reintento()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.Rechaza();

        await using var contexto = escenario.NuevoContexto();
        await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
            .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
        await contexto.SaveChangesAsync();

        // Una sola llamada al proveedor pese a MaxReintentos = 1.
        Assert.Single(payklik.Peticiones);
        var pago = await contexto.Pagos.Include(p => p.Intentos).SingleAsync();
        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Single(pago.Intentos);
    }

    [Fact]
    public async Task Un_5xx_dispara_un_reintento_y_el_segundo_intento_exitoso_deja_el_pago_APLICADO()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.FallaLuegoAprueba(fallas5xx: 1);

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Equal(2, payklik.Peticiones.Count);

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        // Dos filas de intento, numeradas 1 y 2 dentro del mismo pago.
        Assert.Equal(new[] { 1, 2 }, pago.Intentos.OrderBy(i => i.IntentoNumero).Select(i => i.IntentoNumero));
        Assert.Equal(EstadoIntentoPago.FALLIDO, pago.Intentos.Single(i => i.IntentoNumero == 1).Estado);
        Assert.Equal(EstadoIntentoPago.EXITOSO, pago.Intentos.Single(i => i.IntentoNumero == 2).Estado);
        Assert.Equal(EstadoPago.APLICADO, pago.Estado);

        // Solo se publica el evento del desenlace final, no un PagoPendiente intermedio.
        Assert.Empty(escenario.Outbox.PorTipo(PagoPendiente.EventType));
        Assert.Single(escenario.Outbox.PorTipo(PagoAplicado.EventType));
    }

    [Fact]
    public async Task Dos_intentos_fallidos_en_un_cobro_dejan_dos_registros_auditables_y_un_solo_PagoPendiente()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.NoDisponible();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
                .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Equal(2, payklik.Peticiones.Count);

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();
        Assert.Equal(EstadoPago.PENDIENTE, pago.Estado);
        Assert.All(pago.Intentos, intento => Assert.Equal(EstadoIntentoPago.FALLIDO, intento.Estado));

        // Un evento de auditoria por cada intento fallido, con su propio numero.
        var auditables = escenario.Outbox.PorTipo(IntentoPagoFallido.EventType)
            .OrderBy(evento => evento.Sequence)
            .ToList();
        Assert.Equal(2, auditables.Count);
        Assert.All(auditables, evento =>
        {
            Assert.Equal(IntentoPagoFallido.AggregateType, evento.AggregateType);
            Assert.Equal(IntentoPagoFallido.RoutingKey, evento.RoutingKey);
            Assert.Equal(OrdenId, evento.AggregateId);
        });
        var primero = Assert.IsType<IntentoPagoFallidoData>(auditables[0].Data);
        var segundo = Assert.IsType<IntentoPagoFallidoData>(auditables[1].Data);
        Assert.Equal((1, true), (primero.IntentoNumero, primero.SeReintenta));
        Assert.Equal((2, false), (segundo.IntentoNumero, segundo.SeReintenta));
        Assert.Equal("PROVEEDOR_5XX", primero.CodigoRespuesta);

        // La saga ve lo mismo que antes: un solo PagoPendiente, con la secuencia del
        // pago y el numero del ultimo intento.
        var pendiente = Assert.Single(escenario.Outbox.PorTipo(PagoPendiente.EventType));
        Assert.Equal(1, pendiente.Sequence);
        Assert.Equal(1, pago.Secuencia);
        Assert.Equal(2, Assert.IsType<PagoPendienteData>(pendiente.Data).IntentoNumero);
        Assert.Equal(3, escenario.Outbox.Eventos.Count);
    }

    [Fact]
    public async Task Un_intento_fallido_superado_por_el_reintento_queda_auditado_y_PagoAplicado_conserva_su_secuencia()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.FallaLuegoAprueba(fallas5xx: 1);

        await using var contexto = escenario.NuevoContexto();
        await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
            .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
        await contexto.SaveChangesAsync();

        var auditable = Assert.Single(escenario.Outbox.PorTipo(IntentoPagoFallido.EventType));
        var data = Assert.IsType<IntentoPagoFallidoData>(auditable.Data);
        Assert.Equal(1, data.IntentoNumero);
        Assert.True(data.SeReintenta);

        var aplicado = Assert.Single(escenario.Outbox.PorTipo(PagoAplicado.EventType));
        Assert.Equal(1, aplicado.Sequence);
        Assert.Empty(escenario.Outbox.PorTipo(PagoPendiente.EventType));
    }

    [Fact]
    public async Task Un_cobro_aprobado_o_rechazado_sin_fallas_no_emite_IntentoPagoFallido()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Aprueba())
                .ProcesarCobroAsync(Solicitud("ORD-2026-000010"), CancellationToken.None);
            await escenario.Servicio(contexto, ClientePayKlikFalso.Rechaza())
                .ProcesarCobroAsync(Solicitud("ORD-2026-000011"), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Empty(escenario.Outbox.PorTipo(IntentoPagoFallido.EventType));
    }

    [Fact]
    public async Task Los_reintentos_usan_la_misma_Idempotency_Key_el_identificador_del_pago()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.FallaLuegoAprueba(fallas5xx: 1);

        await using var contexto = escenario.NuevoContexto();
        await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
            .ProcesarCobroAsync(Solicitud(), CancellationToken.None);
        await contexto.SaveChangesAsync();

        var pago = await contexto.Pagos.SingleAsync();
        Assert.Equal(2, payklik.ClavesIdempotencia.Count);
        Assert.Equal(pago.Id.ToString(), payklik.ClavesIdempotencia[0]);
        Assert.All(payklik.ClavesIdempotencia, clave => Assert.Equal(pago.Id.ToString(), clave));
    }

    [Fact]
    public void Ningun_tipo_del_contexto_Pagos_expone_campos_de_PAN_CVV_o_vencimiento()
    {
        var tipos = new[]
        {
            typeof(Pago), typeof(IntentoPago), typeof(EnlaceReintento),
            typeof(SolicitudCobro), typeof(PeticionCargoPayKlik), typeof(ResultadoPayKlik),
            typeof(PagoDto), typeof(IntentoPagoDto),
            typeof(PagoAplicadoData), typeof(PagoRechazadoData), typeof(PagoPendienteData),
            typeof(EnlaceReintentoData), typeof(IntentoPagoFallidoData)
        };

        var prohibidos = new[]
        {
            "pan", "cvv", "cvc", "numerotarjeta", "cardnumber",
            "vencimiento", "expiracion", "expiry", "expirationdate", "fechavencimiento"
        };

        foreach (var tipo in tipos)
        {
            foreach (var propiedad in tipo.GetProperties())
            {
                var nombre = propiedad.Name.ToLowerInvariant();
                Assert.DoesNotContain(prohibidos, prohibido => nombre.Contains(prohibido));
            }
        }
    }
}
