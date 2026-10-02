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

/// <summary>
/// Cobro con la boveda transitoria (A4): un token vencido o consumido se rechaza sin
/// llamar a PayKlik; un cobro aprobado con GuardarAlCobrar crea la tarjeta guardada;
/// con PayKlik caido el token queda ACTIVO para el reintento; y una tarjeta guardada
/// cobra con su referencia y el CVV.
/// </summary>
public sealed class CobroBovedaTests
{
    private const string OrdenId = "ORD-2026-000500";
    private const string Ana = "cliente1@sgo.local";

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"pagos-boveda-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public PagosDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<PagosDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        public async Task SembrarAsync(params object[] entidades)
        {
            await using var contexto = NuevoContexto();
            contexto.AddRange(entidades);
            await contexto.SaveChangesAsync();
        }

        public async Task<Pago> CobrarAsync(IClientePayKlik payklik, string tokenId, string clienteId = Ana)
        {
            await using var contexto = NuevoContexto();
            var servicio = new ServicioCobro(
                contexto,
                payklik,
                Outbox,
                Options.Create(new PayKlikOptions { MaxReintentos = 0, BackoffBaseMs = 0 }),
                Options.Create(new ReintentoOptions { ExpiracionMinutos = 60 }),
                BovedaPruebas.Cifrador,
                NullLogger<ServicioCobro>.Instance);

            var pago = await servicio.ProcesarCobroAsync(
                new SolicitudCobro(OrdenId, clienteId, Canal.WEB, Moneda.GTQ, 1899.00m, MedioPago.TARJETA, tokenId, null, null),
                CancellationToken.None);
            await contexto.SaveChangesAsync();
            return pago;
        }
    }

    private static TarjetaGuardada TarjetaDeAna(string referencia = "card_pk_ana") =>
        new(Ana, referencia, MarcaTarjeta.VISA, "4242", 12, DateTime.UtcNow.Year + 2, "Ana Lopez", DateTime.UtcNow.AddDays(-5));

    [Fact]
    public async Task Token_vencido_se_rechaza_sin_llamar_a_PayKlik_y_queda_EXPIRADO_sin_datos()
    {
        var escenario = new Escenario();
        await escenario.SembrarAsync(BovedaPruebas.TokenVencido("tok_vencido", Ana));
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        var pago = await escenario.CobrarAsync(payklik, "tok_vencido");

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Equal(CodigosToken.Vencido, Assert.Single(pago.Intentos).CodigoRespuesta);
        Assert.Empty(payklik.Peticiones);

        await using var verificacion = escenario.NuevoContexto();
        var token = await verificacion.TokensPago.SingleAsync();
        Assert.Equal(EstadoTokenPago.EXPIRADO, token.Estado);
        Assert.Null(token.DatosCifrados);

        var rechazo = Assert.IsType<PagoRechazadoData>(Assert.Single(escenario.Outbox.PorTipo(PagoRechazado.EventType)).Data);
        Assert.Equal(CodigosToken.Vencido, rechazo.CodigoRespuesta);
    }

    [Fact]
    public async Task Token_consumido_se_rechaza_sin_llamar_a_PayKlik()
    {
        var escenario = new Escenario();
        var consumido = BovedaPruebas.TokenNuevo("tok_consumido", clienteId: Ana);
        consumido.Consumir();
        await escenario.SembrarAsync(consumido);
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        var pago = await escenario.CobrarAsync(payklik, "tok_consumido");

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Equal(CodigosToken.YaUsado, Assert.Single(pago.Intentos).CodigoRespuesta);
        Assert.Empty(payklik.Peticiones);
    }

    [Fact]
    public async Task Token_de_otro_cliente_o_inexistente_se_rechaza_como_invalido()
    {
        var escenario = new Escenario();
        await escenario.SembrarAsync(BovedaPruebas.TokenNuevo("tok_de_carlos", clienteId: "cliente2@sgo.local"));
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        var pago = await escenario.CobrarAsync(payklik, "tok_de_carlos");

        Assert.Equal(CodigosToken.Invalido, Assert.Single(pago.Intentos).CodigoRespuesta);
        Assert.Empty(payklik.Peticiones);
    }

    [Fact]
    public async Task Cobro_exitoso_con_GuardarAlCobrar_crea_la_tarjeta_guardada_con_la_referencia_de_PayKlik()
    {
        var escenario = new Escenario();
        await escenario.SembrarAsync(BovedaPruebas.TokenNuevo("tok_guardar", clienteId: Ana, guardarAlCobrar: true));
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik(referenciaGuardada: "card_pk_desde_cobro");

        var pago = await escenario.CobrarAsync(payklik, "tok_guardar");

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.True(Assert.Single(payklik.Peticiones).Guardar);

        await using var verificacion = escenario.NuevoContexto();
        var tarjeta = await verificacion.TarjetasGuardadas.SingleAsync();
        Assert.Equal(Ana, tarjeta.ClienteId);
        Assert.Equal("card_pk_desde_cobro", tarjeta.ReferenciaPayKlik);
        Assert.Equal("4242", tarjeta.Ultimos4);
        Assert.Equal(MarcaTarjeta.VISA, tarjeta.Marca);
        Assert.Equal(BovedaPruebas.Titular, tarjeta.Titular);
        Assert.True(tarjeta.Activa);

        var token = await verificacion.TokensPago.SingleAsync();
        Assert.Equal(EstadoTokenPago.CONSUMIDO, token.Estado);
        Assert.Null(token.DatosCifrados);
    }

    [Fact]
    public async Task Cobro_exitoso_con_GuardarAlCobrar_actualiza_la_tarjeta_que_ya_existia_sin_duplicarla()
    {
        var escenario = new Escenario();
        var existente = TarjetaDeAna("card_pk_vieja");
        var token = BovedaPruebas.TokenNuevo(
            "tok_guardar_otra_vez", clienteId: Ana, guardarAlCobrar: true, mesExp: existente.MesExp, anioExp: existente.AnioExp);
        await escenario.SembrarAsync(existente, token);

        await escenario.CobrarAsync(ClientePayKlikFalso.ApruebaComoPayKlik(referenciaGuardada: "card_pk_renovada"), token.Id);

        await using var verificacion = escenario.NuevoContexto();
        var unica = await verificacion.TarjetasGuardadas.SingleAsync();
        Assert.Equal(existente.Id, unica.Id);
        Assert.Equal("card_pk_renovada", unica.ReferenciaPayKlik);
    }

    [Fact]
    public async Task Un_invitado_no_guarda_tarjeta_aunque_pida_guardarla()
    {
        var escenario = new Escenario();
        await escenario.SembrarAsync(BovedaPruebas.TokenNuevo("tok_invitado", clienteId: null, guardarAlCobrar: true));
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        var pago = await escenario.CobrarAsync(payklik, "tok_invitado", clienteId: "invitado-1@megastore.com");

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.Null(Assert.Single(payklik.Peticiones).Guardar);

        await using var verificacion = escenario.NuevoContexto();
        Assert.Empty(await verificacion.TarjetasGuardadas.ToListAsync());
    }

    [Fact]
    public async Task Con_PayKlik_caido_el_pago_queda_PENDIENTE_y_el_token_sigue_ACTIVO_con_sus_datos()
    {
        var escenario = new Escenario();
        await escenario.SembrarAsync(BovedaPruebas.TokenNuevo("tok_caido", clienteId: Ana));

        var pago = await escenario.CobrarAsync(ClientePayKlikFalso.NoDisponible(), "tok_caido");

        Assert.Equal(EstadoPago.PENDIENTE, pago.Estado);
        Assert.Single(escenario.Outbox.PorTipo(PagoPendiente.EventType));

        await using var verificacion = escenario.NuevoContexto();
        var token = await verificacion.TokensPago.SingleAsync();
        Assert.Equal(EstadoTokenPago.ACTIVO, token.Estado);
        Assert.NotNull(token.DatosCifrados);

        // El reintento cobra con el mismo token, sin pedir datos.
        var reintento = await escenario.CobrarAsync(ClientePayKlikFalso.ApruebaComoPayKlik(), "tok_caido");
        Assert.Equal(EstadoPago.APLICADO, reintento.Estado);
    }

    [Fact]
    public async Task Tarjeta_guardada_cobra_con_su_referencia_y_el_cvv_sin_numero()
    {
        var escenario = new Escenario();
        var tarjeta = TarjetaDeAna();
        await escenario.SembrarAsync(tarjeta, BovedaPruebas.TokenGuardada("tok_guardada", tarjeta, cvv: "456"));
        var payklik = ClientePayKlikFalso.Aprueba();

        var pago = await escenario.CobrarAsync(payklik, "tok_guardada");

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        var peticion = Assert.Single(payklik.Peticiones);
        Assert.Null(peticion.Tarjeta);
        Assert.Equal("card_pk_ana", peticion.ReferenciaTarjeta);
        Assert.Equal("456", peticion.Cvv);
    }

    [Fact]
    public async Task Si_PayKlik_ya_no_tiene_la_referencia_el_pago_se_rechaza_y_la_tarjeta_se_desactiva()
    {
        var escenario = new Escenario();
        var tarjeta = TarjetaDeAna();
        await escenario.SembrarAsync(tarjeta, BovedaPruebas.TokenGuardada("tok_perdida", tarjeta));

        var pago = await escenario.CobrarAsync(ClientePayKlikFalso.TokenNoValido(CodigosToken.TarjetaNoEncontrada), "tok_perdida");

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Equal(CodigosToken.TarjetaNoEncontrada, Assert.Single(pago.Intentos).CodigoRespuesta);

        await using var verificacion = escenario.NuevoContexto();
        Assert.False((await verificacion.TarjetasGuardadas.SingleAsync()).Activa);
    }

    [Fact]
    public async Task Un_token_con_los_datos_alterados_se_rechaza_como_invalido_sin_llamar_a_PayKlik()
    {
        var escenario = new Escenario();
        var token = BovedaPruebas.TokenNuevo("tok_alterado", clienteId: Ana);
        token.Tag![0] ^= 0xFF;
        await escenario.SembrarAsync(token);
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        var pago = await escenario.CobrarAsync(payklik, "tok_alterado");

        Assert.Equal(CodigosToken.Invalido, Assert.Single(pago.Intentos).CodigoRespuesta);
        Assert.Empty(payklik.Peticiones);
    }
}
