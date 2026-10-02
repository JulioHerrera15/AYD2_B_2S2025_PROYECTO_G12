using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Controllers;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

public sealed class PagosControllerTests
{
    private const string OrdenId = "ORD-2026-REINTENTO-01";
    private const string ClienteId = "CLI-000100";

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"pagos-controller-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public PagosDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<PagosDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        public PagosController CrearController(PagosDbContext contexto, IClientePayKlik payklik)
        {
            var servicioCobro = new ServicioCobro(
                contexto,
                payklik,
                Outbox,
                Options.Create(new PayKlikOptions { MaxReintentos = 0, BackoffBaseMs = 0 }),
                Options.Create(new ReintentoOptions { ExpiracionMinutos = 60 }),
                BovedaPruebas.Cifrador,
                NullLogger<ServicioCobro>.Instance);

            var tokens = new ServicioTokensPago(
                new RepositorioTokensPago(contexto),
                new RepositorioTarjetasGuardadas(contexto),
                BovedaPruebas.Cifrador,
                Options.Create(new BovedaOptions()),
                NullLogger<ServicioTokensPago>.Instance);

            var disyuntor = new DisyuntorPayKlik(Options.Create(new PayKlikOptions()));

            return new PagosController(contexto, servicioCobro, tokens, disyuntor);
        }
    }

    /// <summary>Pago PENDIENTE con un intento fallido hecho con <paramref name="token" /> y un enlace vigente.</summary>
    private static async Task<EnlaceReintento> SembrarPendienteAsync(PagosDbContext contexto, TokenPago token, string clienteId = ClienteId)
    {
        var pago = new Pago(OrdenId, clienteId, Canal.WEB, Moneda.GTQ, 500.00m, MedioPago.TARJETA);
        pago.MarcarPendiente();
        var intento = pago.RegistrarIntento(MedioPago.TARJETA, token.Id, token.Ultimos4, token.Marca.ToString());
        intento.MarcarFallido("PROVEEDOR_5XX", "PayKlik respondio 503.", 10, DateTime.UtcNow);
        var enlace = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));

        contexto.Pagos.Add(pago);
        contexto.Intentos.Add(intento);
        contexto.EnlacesReintento.Add(enlace);
        contexto.TokensPago.Add(token);
        await contexto.SaveChangesAsync();
        return enlace;
    }

    // ── A5: reintento con token interno vigente o vencido ──────────────────────

    [Fact]
    public async Task Reintentar_con_el_token_vigente_cobra_sin_pedir_datos()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();
        var enlace = await SembrarPendienteAsync(contexto, BovedaPruebas.TokenNuevo("tok_vigente"));
        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.ApruebaComoPayKlik());

        var info = Assert.IsType<EnlaceReintentoInfoDto>(
            Assert.IsType<OkObjectResult>((await controller.ObtenerInfoReintento(enlace.Token, CancellationToken.None)).Result).Value);
        Assert.Equal(DatosRequeridosReintento.Ninguno, info.DatosRequeridos);

        var resultado = await controller.Reintentar(enlace.Token, null, CancellationToken.None);

        var pagoDto = Assert.IsType<PagoDto>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(EstadoPago.APLICADO, pagoDto.Estado);
    }

    [Fact]
    public async Task Reintentar_con_token_vencido_pide_la_tarjeta_completa_sin_cobrar_ni_rechazar_el_pago()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();
        var enlace = await SembrarPendienteAsync(contexto, BovedaPruebas.TokenVencido("tok_vencido"));
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();
        var controller = escenario.CrearController(contexto, payklik);

        var info = Assert.IsType<EnlaceReintentoInfoDto>(
            Assert.IsType<OkObjectResult>((await controller.ObtenerInfoReintento(enlace.Token, CancellationToken.None)).Result).Value);
        Assert.Equal(DatosRequeridosReintento.Tarjeta, info.DatosRequeridos);

        var resultado = await controller.Reintentar(enlace.Token, null, CancellationToken.None);

        var problema = Assert.IsType<ObjectResult>(resultado.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problema.StatusCode);
        var detalle = Assert.IsType<ProblemDetails>(problema.Value);
        Assert.Equal("datos_tarjeta_requeridos", detalle.Extensions["codigo"]);
        Assert.Equal(DatosRequeridosReintento.Tarjeta, detalle.Extensions["datosRequeridos"]);

        Assert.Empty(payklik.Peticiones);
        Assert.Equal(EstadoPago.PENDIENTE, (await contexto.Pagos.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Reintentar_con_un_token_nuevo_en_el_cuerpo_cobra_con_esa_tarjeta()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();
        var enlace = await SembrarPendienteAsync(contexto, BovedaPruebas.TokenVencido("tok_vencido"));
        contexto.TokensPago.Add(BovedaPruebas.TokenNuevo("tok_nuevo", numero: "5555555555554444"));
        await contexto.SaveChangesAsync();
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();
        var controller = escenario.CrearController(contexto, payklik);

        var resultado = await controller.Reintentar(enlace.Token, new ReintentoPagoRequest("tok_nuevo", null), CancellationToken.None);

        var pagoDto = Assert.IsType<PagoDto>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(EstadoPago.APLICADO, pagoDto.Estado);
        Assert.Equal("5555555555554444", Assert.Single(payklik.Peticiones).Tarjeta!.Numero);
    }

    [Fact]
    public async Task Reintentar_con_token_vencido_de_tarjeta_guardada_pide_solo_el_CVV_y_cobra_con_el()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();
        var tarjeta = new TarjetaGuardada(ClienteId, "card_pk_guardada", MarcaTarjeta.VISA, "4242", 12, DateTime.UtcNow.Year + 2, "Ana Lopez", DateTime.UtcNow);
        contexto.TarjetasGuardadas.Add(tarjeta);
        var vencido = BovedaPruebas.TokenGuardada("tok_guardada_vencida", tarjeta, creadoEn: DateTime.UtcNow.AddMinutes(-31));
        var enlace = await SembrarPendienteAsync(contexto, vencido);
        var payklik = ClientePayKlikFalso.Aprueba();
        var controller = escenario.CrearController(contexto, payklik);

        var info = Assert.IsType<EnlaceReintentoInfoDto>(
            Assert.IsType<OkObjectResult>((await controller.ObtenerInfoReintento(enlace.Token, CancellationToken.None)).Result).Value);
        Assert.Equal(DatosRequeridosReintento.Cvv, info.DatosRequeridos);

        var resultado = await controller.Reintentar(enlace.Token, new ReintentoPagoRequest(null, "987"), CancellationToken.None);

        var pagoDto = Assert.IsType<PagoDto>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(EstadoPago.APLICADO, pagoDto.Estado);
        var peticion = Assert.Single(payklik.Peticiones);
        Assert.Equal("card_pk_guardada", peticion.ReferenciaTarjeta);
        Assert.Equal("987", peticion.Cvv);
    }

    [Fact]
    public async Task ObtenerInfoReintento_con_pago_pendiente_retorna_enlace_vigente_y_no_usado()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();

        var pago = new Pago(OrdenId, ClienteId, Canal.WEB, Moneda.GTQ, 350.00m, MedioPago.TARJETA);
        pago.MarcarPendiente();
        var enlace = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));

        contexto.Pagos.Add(pago);
        contexto.EnlacesReintento.Add(enlace);
        await contexto.SaveChangesAsync();

        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Aprueba());

        var resultado = await controller.ObtenerInfoReintento(enlace.Token, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(resultado.Result);
        var info = Assert.IsType<EnlaceReintentoInfoDto>(okResult.Value);

        Assert.Equal(enlace.Token, info.Token);
        Assert.Equal(OrdenId, info.OrdenId);
        Assert.True(info.EstaVigente);
        Assert.False(info.Usado);
        Assert.Equal("PENDIENTE", info.EstadoPago);
    }

    [Fact]
    public async Task ObtenerInfoReintento_con_orden_ya_pagada_informa_estado_APLICADO_y_usado()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();

        var pago = new Pago(OrdenId, ClienteId, Canal.WEB, Moneda.GTQ, 350.00m, MedioPago.TARJETA);
        pago.MarcarAplicado();
        var enlace = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));

        contexto.Pagos.Add(pago);
        contexto.EnlacesReintento.Add(enlace);
        await contexto.SaveChangesAsync();

        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Aprueba());

        var resultado = await controller.ObtenerInfoReintento(enlace.Token, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(resultado.Result);
        var info = Assert.IsType<EnlaceReintentoInfoDto>(okResult.Value);

        Assert.False(info.EstaVigente);
        Assert.True(info.Usado);
        Assert.Equal("APLICADO", info.EstadoPago);
    }

    [Fact]
    public async Task Reintentar_cuando_proveedor_falla_no_invalida_el_enlace_actual()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();

        var pago = new Pago(OrdenId, ClienteId, Canal.WEB, Moneda.GTQ, 500.00m, MedioPago.TARJETA);
        pago.MarcarPendiente();
        var intento = pago.RegistrarIntento(MedioPago.TARJETA, "tok_pk_test1", "1234", "VISA");
        intento.MarcarFallido("TIMEOUT", "PayKlik timeout", 2000, DateTime.UtcNow);
        var enlace = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));

        contexto.Pagos.Add(pago);
        contexto.Intentos.Add(intento);
        contexto.TokensPago.Add(BovedaPruebas.TokenNuevo(intento.TokenMedioPago!));
        contexto.EnlacesReintento.Add(enlace);
        await contexto.SaveChangesAsync();

        // El proveedor vuelve a fallar con timeout
        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Timeout());

        var resultado = await controller.Reintentar(enlace.Token, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(resultado.Result);
        var pagoDto = Assert.IsType<PagoDto>(okResult.Value);
        Assert.Equal(EstadoPago.PENDIENTE, pagoDto.Estado);

        // El enlace no debe haber quedado quemado como Usado = true
        var enlaceDb = await contexto.EnlacesReintento.SingleAsync(e => e.Token == enlace.Token);
        Assert.False(enlaceDb.Usado);

        // Se genero un segundo enlace para la orden
        var totalEnlaces = await contexto.EnlacesReintento.Where(e => e.OrdenId == OrdenId).CountAsync();
        Assert.Equal(2, totalEnlaces);
    }

    [Fact]
    public async Task Reintentar_exitoso_marca_todos_los_enlaces_de_la_orden_como_usados()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();

        var pago = new Pago(OrdenId, ClienteId, Canal.WEB, Moneda.GTQ, 500.00m, MedioPago.TARJETA);
        pago.MarcarPendiente();
        var intento = pago.RegistrarIntento(MedioPago.TARJETA, "tok_pk_test2", "4321", "VISA");
        var enlace1 = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));
        var enlace2 = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));

        contexto.Pagos.Add(pago);
        contexto.Intentos.Add(intento);
        contexto.TokensPago.Add(BovedaPruebas.TokenNuevo(intento.TokenMedioPago!));
        contexto.EnlacesReintento.AddRange(enlace1, enlace2);
        await contexto.SaveChangesAsync();

        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Aprueba());

        var resultado = await controller.Reintentar(enlace1.Token, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(resultado.Result);
        var pagoDto = Assert.IsType<PagoDto>(okResult.Value);
        Assert.Equal(EstadoPago.APLICADO, pagoDto.Estado);

        // Ambos enlaces deben quedar marcados como usados
        var enlacesDb = await contexto.EnlacesReintento.Where(e => e.OrdenId == OrdenId).ToListAsync();
        Assert.All(enlacesDb, e => Assert.True(e.Usado));
    }

    [Fact]
    public async Task Reintentar_cuando_orden_ya_estaba_pagada_retorna_pago_sin_nuevo_cobro()
    {
        var escenario = new Escenario();
        await using var contexto = escenario.NuevoContexto();

        var pago = new Pago(OrdenId, ClienteId, Canal.WEB, Moneda.GTQ, 500.00m, MedioPago.TARJETA);
        pago.MarcarAplicado();
        var enlace = pago.GenerarEnlaceReintento(TimeSpan.FromHours(1));

        contexto.Pagos.Add(pago);
        contexto.EnlacesReintento.Add(enlace);
        await contexto.SaveChangesAsync();

        // El cliente de PayKlik fallaria si fuera invocado
        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Timeout());

        var resultado = await controller.Reintentar(enlace.Token, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(resultado.Result);
        var pagoDto = Assert.IsType<PagoDto>(okResult.Value);
        Assert.Equal(EstadoPago.APLICADO, pagoDto.Estado);

        var enlaceDb = await contexto.EnlacesReintento.SingleAsync(e => e.Token == enlace.Token);
        Assert.True(enlaceDb.Usado);
    }
}
