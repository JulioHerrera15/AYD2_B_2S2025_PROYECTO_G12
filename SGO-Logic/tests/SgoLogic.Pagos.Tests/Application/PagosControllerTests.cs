using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
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
                NullLogger<ServicioCobro>.Instance);

            var disyuntor = new DisyuntorPayKlik(Options.Create(new PayKlikOptions()));

            return new PagosController(contexto, servicioCobro, disyuntor);
        }
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
        contexto.EnlacesReintento.Add(enlace);
        await contexto.SaveChangesAsync();

        // El proveedor vuelve a fallar con timeout
        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Timeout());

        var resultado = await controller.Reintentar(enlace.Token, CancellationToken.None);

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
        contexto.EnlacesReintento.AddRange(enlace1, enlace2);
        await contexto.SaveChangesAsync();

        var controller = escenario.CrearController(contexto, ClientePayKlikFalso.Aprueba());

        var resultado = await controller.Reintentar(enlace1.Token, CancellationToken.None);

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

        var resultado = await controller.Reintentar(enlace.Token, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(resultado.Result);
        var pagoDto = Assert.IsType<PagoDto>(okResult.Value);
        Assert.Equal(EstadoPago.APLICADO, pagoDto.Estado);

        var enlaceDb = await contexto.EnlacesReintento.SingleAsync(e => e.Token == enlace.Token);
        Assert.True(enlaceDb.Usado);
    }
}
