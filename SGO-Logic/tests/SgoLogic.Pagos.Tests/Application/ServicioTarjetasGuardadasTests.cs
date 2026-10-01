using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Controllers;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// Tarjetas guardadas: upsert sin duplicados, aislamiento por cliente y reaccion a
/// una referencia que PayKlik ya no reconoce.
/// </summary>
public sealed class ServicioTarjetasGuardadasTests
{
    private const string Ana = "cliente1@sgo.local";
    private const string Carlos = "cliente2@sgo.local";

    private static readonly DateTime Ahora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TarjetaGuardada Tarjeta(
        string cliente = Ana,
        MarcaTarjeta marca = MarcaTarjeta.VISA,
        string ultimos4 = "1111",
        int mes = 12,
        int anio = 2030,
        string referencia = "card_pk_a1") =>
        new(cliente, referencia, marca, ultimos4, mes, anio, "Ana Lopez", Ahora.AddDays(-10));

    private static (ServicioTarjetasGuardadas Servicio, RepositorioTarjetasGuardadasFalso Repositorio, ClienteBovedaPayKlikFalso Boveda) Construir(
        params TarjetaGuardada[] tarjetas)
    {
        var repositorio = new RepositorioTarjetasGuardadasFalso().Precargar(tarjetas);
        var boveda = new ClienteBovedaPayKlikFalso();
        var servicio = new ServicioTarjetasGuardadas(
            repositorio,
            boveda,
            NullLogger<ServicioTarjetasGuardadas>.Instance,
            () => Ahora);

        return (servicio, repositorio, boveda);
    }

    [Fact]
    public async Task Guardar_la_misma_tarjeta_otra_vez_actualiza_la_referencia_sin_duplicar()
    {
        var (servicio, repositorio, _) = Construir();

        var (primera, estadoPrimera) = await servicio.GuardarAsync(
            Ana, new GuardarTarjetaRequest("card_pk_vieja", "VISA", "1111", 12, 2030, "Ana Lopez"), CancellationToken.None);
        var (segunda, estadoSegunda) = await servicio.GuardarAsync(
            Ana, new GuardarTarjetaRequest("card_pk_nueva", "VISA", "1111", 12, 30, "Ana Lopez"), CancellationToken.None);

        Assert.Equal(EstadoGuardarTarjeta.Creada, estadoPrimera);
        Assert.Equal(EstadoGuardarTarjeta.Actualizada, estadoSegunda);
        Assert.Equal(primera.Id, segunda.Id);

        var unica = Assert.Single(repositorio.Todas);
        Assert.Equal("card_pk_nueva", unica.ReferenciaPayKlik);
        Assert.Equal(2030, unica.AnioExp);
    }

    [Fact]
    public async Task Otra_tarjeta_o_la_misma_de_otro_cliente_si_se_guarda_aparte()
    {
        var (servicio, repositorio, _) = Construir();

        await servicio.GuardarAsync(Ana, new GuardarTarjetaRequest("card_pk_1", "VISA", "1111", 12, 2030, null), CancellationToken.None);
        await servicio.GuardarAsync(Ana, new GuardarTarjetaRequest("card_pk_2", "AMEX", "0005", 12, 2030, null), CancellationToken.None);
        await servicio.GuardarAsync(Carlos, new GuardarTarjetaRequest("card_pk_3", "VISA", "1111", 12, 2030, null), CancellationToken.None);

        Assert.Equal(3, repositorio.Todas.Count);
    }

    [Fact]
    public async Task Pedir_token_de_una_tarjeta_de_otro_cliente_devuelve_no_encontrada_sin_llamar_a_PayKlik()
    {
        var deCarlos = Tarjeta(cliente: Carlos);
        var (servicio, _, boveda) = Construir(deCarlos);

        var resultado = await servicio.ObtenerTokenAsync(Ana, deCarlos.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjeta.NoEncontrada, resultado.Estado);
        Assert.Null(resultado.Token);
        Assert.Empty(boveda.ReferenciasConsultadas);
        Assert.Null(deCarlos.UltimoUsoEn);
    }

    [Fact]
    public async Task Si_PayKlik_responde_404_la_tarjeta_se_desactiva_y_se_pide_agregarla_de_nuevo()
    {
        var tarjeta = Tarjeta();
        var (servicio, repositorio, boveda) = Construir(tarjeta);
        boveda.Responder = EstadoTokenBoveda.ReferenciaNoExiste;

        var resultado = await servicio.ObtenerTokenAsync(Ana, tarjeta.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjeta.YaNoValida, resultado.Estado);
        Assert.Equal("La tarjeta ya no es valida, agregala de nuevo", resultado.Mensaje);
        Assert.False(tarjeta.Activa);
        Assert.Equal(1, repositorio.Guardados);
        Assert.Empty(await servicio.ListarAsync(Ana, CancellationToken.None));
    }

    [Fact]
    public async Task Token_emitido_actualiza_el_ultimo_uso()
    {
        var tarjeta = Tarjeta();
        var (servicio, _, boveda) = Construir(tarjeta);

        var resultado = await servicio.ObtenerTokenAsync(Ana, tarjeta.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjeta.Emitido, resultado.Estado);
        Assert.NotNull(resultado.Token);
        Assert.Equal(Ahora, tarjeta.UltimoUsoEn);
        Assert.Equal(new[] { "card_pk_a1" }, boveda.ReferenciasConsultadas);
    }

    [Fact]
    public async Task Una_tarjeta_vencida_no_pide_token()
    {
        var vencida = Tarjeta(mes: 8, anio: 2026);
        var (servicio, _, boveda) = Construir(vencida);

        var resultado = await servicio.ObtenerTokenAsync(Ana, vencida.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjeta.Vencida, resultado.Estado);
        Assert.Empty(boveda.ReferenciasConsultadas);

        var listada = Assert.Single(await servicio.ListarAsync(Ana, CancellationToken.None));
        Assert.True(listada.Vencida);
        Assert.Equal("**** **** **** 1111", listada.Enmascarado);
    }

    [Theory]
    [InlineData(MarcaTarjeta.AMEX, "0005", "123", EstadoTokenTarjeta.CvvInvalido)]
    [InlineData(MarcaTarjeta.AMEX, "0005", "1234", EstadoTokenTarjeta.Emitido)]
    [InlineData(MarcaTarjeta.VISA, "1111", "1234", EstadoTokenTarjeta.CvvInvalido)]
    [InlineData(MarcaTarjeta.MASTERCARD, "4444", "123", EstadoTokenTarjeta.Emitido)]
    public async Task El_CVV_se_valida_segun_la_marca_antes_de_ir_a_PayKlik(
        MarcaTarjeta marca, string ultimos4, string cvv, EstadoTokenTarjeta esperado)
    {
        var tarjeta = Tarjeta(marca: marca, ultimos4: ultimos4);
        var (servicio, _, boveda) = Construir(tarjeta);

        var resultado = await servicio.ObtenerTokenAsync(Ana, tarjeta.Id, cvv, CancellationToken.None);

        Assert.Equal(esperado, resultado.Estado);
        Assert.Equal(esperado == EstadoTokenTarjeta.Emitido ? 1 : 0, boveda.ReferenciasConsultadas.Count);
    }

    // ── Controlador: el cliente sale del JWT ───────────────────────────────────

    private static TarjetasController Controlador(ServicioTarjetasGuardadas servicio, string email)
    {
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }, "Bearer"));
        return new TarjetasController(servicio)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = usuario } }
        };
    }

    [Fact]
    public async Task POST_token_de_la_tarjeta_de_otro_cliente_responde_404()
    {
        var deCarlos = Tarjeta(cliente: Carlos);
        var (servicio, _, _) = Construir(deCarlos);

        var respuesta = await Controlador(servicio, Ana).Token(
            deCarlos.Id, new SolicitarTokenTarjetaRequest("123"), CancellationToken.None);

        var objeto = Assert.IsType<ObjectResult>(respuesta.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objeto.StatusCode);
    }

    [Fact]
    public async Task POST_token_cuando_PayKlik_ya_no_la_reconoce_responde_410()
    {
        var tarjeta = Tarjeta();
        var (servicio, _, boveda) = Construir(tarjeta);
        boveda.Responder = EstadoTokenBoveda.ReferenciaNoExiste;

        var respuesta = await Controlador(servicio, "Cliente1@SGO.local").Token(
            tarjeta.Id, new SolicitarTokenTarjetaRequest("123"), CancellationToken.None);

        var objeto = Assert.IsType<ObjectResult>(respuesta.Result);
        Assert.Equal(StatusCodes.Status410Gone, objeto.StatusCode);
        Assert.Equal("La tarjeta ya no es valida, agregala de nuevo", Assert.IsType<ProblemDetails>(objeto.Value).Detail);
    }

    [Fact]
    public void El_CVV_no_se_imprime_si_la_solicitud_termina_en_un_log()
    {
        Assert.DoesNotContain("321", new SolicitarTokenTarjetaRequest("321").ToString());
    }
}
