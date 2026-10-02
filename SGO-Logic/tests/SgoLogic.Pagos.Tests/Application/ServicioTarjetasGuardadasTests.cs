using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Controllers;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// Tarjetas guardadas: upsert sin duplicados, aislamiento por cliente y token de la
/// boveda transitoria desde una tarjeta guardada (sin llamar a PayKlik).
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

    private sealed record Montaje(
        ServicioTarjetasGuardadas Tarjetas,
        ServicioTokensPago Tokens,
        RepositorioTarjetasGuardadasFalso RepositorioTarjetas,
        RepositorioTokensPagoFalso RepositorioTokens);

    private static Montaje Construir(params TarjetaGuardada[] tarjetas)
    {
        var repositorioTarjetas = new RepositorioTarjetasGuardadasFalso().Precargar(tarjetas);
        var repositorioTokens = new RepositorioTokensPagoFalso();

        return new Montaje(
            new ServicioTarjetasGuardadas(repositorioTarjetas, () => Ahora),
            new ServicioTokensPago(
                repositorioTokens,
                repositorioTarjetas,
                BovedaPruebas.Cifrador,
                Options.Create(new BovedaOptions { TokenTtlMinutos = 30 }),
                NullLogger<ServicioTokensPago>.Instance,
                () => Ahora),
            repositorioTarjetas,
            repositorioTokens);
    }

    [Fact]
    public async Task Guardar_la_misma_tarjeta_otra_vez_actualiza_la_referencia_sin_duplicar()
    {
        var montaje = Construir();

        var (primera, estadoPrimera) = await montaje.Tarjetas.GuardarAsync(
            Ana, new GuardarTarjetaRequest("card_pk_vieja", "VISA", "1111", 12, 2030, "Ana Lopez"), CancellationToken.None);
        var (segunda, estadoSegunda) = await montaje.Tarjetas.GuardarAsync(
            Ana, new GuardarTarjetaRequest("card_pk_nueva", "VISA", "1111", 12, 30, "Ana Lopez"), CancellationToken.None);

        Assert.Equal(EstadoGuardarTarjeta.Creada, estadoPrimera);
        Assert.Equal(EstadoGuardarTarjeta.Actualizada, estadoSegunda);
        Assert.Equal(primera.Id, segunda.Id);

        var unica = Assert.Single(montaje.RepositorioTarjetas.Todas);
        Assert.Equal("card_pk_nueva", unica.ReferenciaPayKlik);
        Assert.Equal(2030, unica.AnioExp);
    }

    [Fact]
    public async Task Otra_tarjeta_o_la_misma_de_otro_cliente_si_se_guarda_aparte()
    {
        var montaje = Construir();

        await montaje.Tarjetas.GuardarAsync(Ana, new GuardarTarjetaRequest("card_pk_1", "VISA", "1111", 12, 2030, null), CancellationToken.None);
        await montaje.Tarjetas.GuardarAsync(Ana, new GuardarTarjetaRequest("card_pk_2", "AMEX", "0005", 12, 2030, null), CancellationToken.None);
        await montaje.Tarjetas.GuardarAsync(Carlos, new GuardarTarjetaRequest("card_pk_3", "VISA", "1111", 12, 2030, null), CancellationToken.None);

        Assert.Equal(3, montaje.RepositorioTarjetas.Todas.Count);
    }

    [Fact]
    public async Task Eliminar_la_tarjeta_de_otro_cliente_no_hace_nada()
    {
        var deCarlos = Tarjeta(cliente: Carlos);
        var montaje = Construir(deCarlos);

        Assert.False(await montaje.Tarjetas.EliminarAsync(Ana, deCarlos.Id, CancellationToken.None));
        Assert.True(deCarlos.Activa);
    }

    [Fact]
    public async Task Pedir_token_de_una_tarjeta_de_otro_cliente_devuelve_no_encontrada()
    {
        var deCarlos = Tarjeta(cliente: Carlos);
        var montaje = Construir(deCarlos);

        var resultado = await montaje.Tokens.TokenizarTarjetaGuardadaAsync(Ana, deCarlos.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjetaGuardada.NoEncontrada, resultado.Estado);
        Assert.Null(resultado.Token);
        Assert.Empty(montaje.RepositorioTokens.Todos);
        Assert.Null(deCarlos.UltimoUsoEn);
    }

    [Fact]
    public async Task Token_de_tarjeta_guardada_cifra_solo_el_CVV_y_actualiza_el_ultimo_uso()
    {
        var tarjeta = Tarjeta();
        var montaje = Construir(tarjeta);

        var resultado = await montaje.Tokens.TokenizarTarjetaGuardadaAsync(Ana, tarjeta.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjetaGuardada.Emitido, resultado.Estado);
        Assert.StartsWith(TokenPago.Prefijo, resultado.Token!.Token);
        Assert.Equal("1111", resultado.Token.Ultimos4);
        Assert.Equal("VISA", resultado.Token.Marca);
        Assert.Equal(Ahora.AddMinutes(30), resultado.Token.ExpiraEn);
        Assert.Equal(Ahora, tarjeta.UltimoUsoEn);

        var token = Assert.Single(montaje.RepositorioTokens.Todos);
        Assert.Equal(tarjeta.Id, token.ReferenciaTarjetaGuardadaId);
        Assert.False(token.GuardarAlCobrar);

        var secreto = BovedaPruebas.Cifrador.Descifrar(token.ObtenerDatosCifrados(), token.Id);
        Assert.Null(secreto.Numero);
        Assert.Equal("123", secreto.Cvv);
    }

    [Fact]
    public async Task Una_tarjeta_vencida_no_emite_token()
    {
        var vencida = Tarjeta(mes: 8, anio: 2026);
        var montaje = Construir(vencida);

        var resultado = await montaje.Tokens.TokenizarTarjetaGuardadaAsync(Ana, vencida.Id, "123", CancellationToken.None);

        Assert.Equal(EstadoTokenTarjetaGuardada.Vencida, resultado.Estado);
        Assert.Empty(montaje.RepositorioTokens.Todos);

        var listada = Assert.Single(await montaje.Tarjetas.ListarAsync(Ana, CancellationToken.None));
        Assert.True(listada.Vencida);
        Assert.Equal("**** **** **** 1111", listada.Enmascarado);
    }

    [Theory]
    [InlineData(MarcaTarjeta.AMEX, "0005", "123", EstadoTokenTarjetaGuardada.CvvInvalido)]
    [InlineData(MarcaTarjeta.AMEX, "0005", "1234", EstadoTokenTarjetaGuardada.Emitido)]
    [InlineData(MarcaTarjeta.VISA, "1111", "1234", EstadoTokenTarjetaGuardada.CvvInvalido)]
    [InlineData(MarcaTarjeta.MASTERCARD, "4444", "123", EstadoTokenTarjetaGuardada.Emitido)]
    public async Task El_CVV_se_valida_segun_la_marca(
        MarcaTarjeta marca, string ultimos4, string cvv, EstadoTokenTarjetaGuardada esperado)
    {
        var tarjeta = Tarjeta(marca: marca, ultimos4: ultimos4);
        var montaje = Construir(tarjeta);

        var resultado = await montaje.Tokens.TokenizarTarjetaGuardadaAsync(Ana, tarjeta.Id, cvv, CancellationToken.None);

        Assert.Equal(esperado, resultado.Estado);
        Assert.Equal(esperado == EstadoTokenTarjetaGuardada.Emitido ? 1 : 0, montaje.RepositorioTokens.Todos.Count);
    }

    // ── Controlador: el cliente sale del JWT ───────────────────────────────────

    private static TarjetasController Controlador(Montaje montaje, string email)
    {
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }, "Bearer"));
        return new TarjetasController(montaje.Tarjetas, montaje.Tokens)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = usuario } }
        };
    }

    [Fact]
    public async Task POST_token_de_la_tarjeta_de_otro_cliente_responde_404()
    {
        var deCarlos = Tarjeta(cliente: Carlos);
        var montaje = Construir(deCarlos);

        var respuesta = await Controlador(montaje, Ana).Token(
            deCarlos.Id, new SolicitarTokenTarjetaRequest("123"), CancellationToken.None);

        var objeto = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status404NotFound, objeto.StatusCode);
    }

    [Fact]
    public async Task POST_token_con_CVV_invalido_responde_422_con_el_error_en_su_campo()
    {
        var tarjeta = Tarjeta(marca: MarcaTarjeta.AMEX, ultimos4: "0005");
        var montaje = Construir(tarjeta);

        var respuesta = await Controlador(montaje, "Cliente1@SGO.local").Token(
            tarjeta.Id, new SolicitarTokenTarjetaRequest("123"), CancellationToken.None);

        var objeto = Assert.IsType<UnprocessableEntityObjectResult>(respuesta);
        var errores = Assert.IsType<Dictionary<string, string>>(objeto.Value!.GetType().GetProperty("errores")!.GetValue(objeto.Value));
        Assert.Contains("4 digitos", errores["cvv"]);
    }

    [Fact]
    public async Task POST_token_valido_responde_201_con_token_marca_ultimos4_y_vencimiento()
    {
        var tarjeta = Tarjeta();
        var montaje = Construir(tarjeta);

        var respuesta = await Controlador(montaje, Ana).Token(
            tarjeta.Id, new SolicitarTokenTarjetaRequest("123"), CancellationToken.None);

        var objeto = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status201Created, objeto.StatusCode);
        var token = Assert.IsType<TokenPagoDto>(objeto.Value);
        Assert.Equal("1111", token.Ultimos4);
    }

    [Fact]
    public void El_CVV_no_se_imprime_si_la_solicitud_termina_en_un_log()
    {
        Assert.DoesNotContain("321", new SolicitarTokenTarjetaRequest("321").ToString());
    }
}
