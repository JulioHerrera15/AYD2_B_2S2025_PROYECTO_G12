using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Controllers;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// A2/A3: tokenizacion de tarjetas nuevas en la boveda transitoria (sin PayKlik) y
/// expiracion de los tokens vencidos.
/// </summary>
public sealed class ServicioTokensPagoTests
{
    private const string Ana = "cliente1@sgo.local";

    private static readonly DateTime Ahora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SolicitudTokenPago Valida(bool guardar = false) =>
        new("4242 4242 4242 4242", 12, 2029, "731", "Ana Lopez", guardar);

    private static (ServicioTokensPago Servicio, RepositorioTokensPagoFalso Tokens) Construir(Func<DateTime>? reloj = null)
    {
        var tokens = new RepositorioTokensPagoFalso();
        var servicio = new ServicioTokensPago(
            tokens,
            new RepositorioTarjetasGuardadasFalso(),
            BovedaPruebas.Cifrador,
            Options.Create(new BovedaOptions { TokenTtlMinutos = 30 }),
            NullLogger<ServicioTokensPago>.Instance,
            reloj ?? (() => Ahora));
        return (servicio, tokens);
    }

    [Fact]
    public async Task Tarjeta_valida_crea_un_token_opaco_con_PAN_y_CVV_cifrados()
    {
        var (servicio, tokens) = Construir();

        var resultado = await servicio.TokenizarAsync(Valida(), Ana, CancellationToken.None);

        Assert.True(resultado.Exito);
        Assert.Matches("^tok_[0-9a-f]{32}$", resultado.Token!.Token);
        Assert.Equal("VISA", resultado.Token.Marca);
        Assert.Equal("4242", resultado.Token.Ultimos4);
        Assert.Equal(Ahora.AddMinutes(30), resultado.Token.ExpiraEn);

        var token = Assert.Single(tokens.Todos);
        Assert.Equal(Ana, token.ClienteId);
        Assert.Equal(EstadoTokenPago.ACTIVO, token.Estado);
        Assert.Equal(1, tokens.Guardados);

        var secreto = BovedaPruebas.Cifrador.Descifrar(token.ObtenerDatosCifrados(), token.Id);
        Assert.Equal("4242424242424242", secreto.Numero);
        Assert.Equal("731", secreto.Cvv);
    }

    [Fact]
    public async Task Tarjeta_invalida_devuelve_errores_por_campo_y_no_crea_token()
    {
        var (servicio, tokens) = Construir();

        var resultado = await servicio.TokenizarAsync(
            new SolicitudTokenPago("4242424242424241", 13, 2029, "12", "A", Guardar: false), Ana, CancellationToken.None);

        Assert.False(resultado.Exito);
        Assert.Equal(new[] { "cvv", "mesExp", "numero", "titular" }, resultado.Errores.Keys.OrderBy(k => k));
        Assert.Empty(tokens.Todos);
    }

    [Fact]
    public async Task Guardar_solo_se_respeta_con_un_cliente_autenticado()
    {
        var (servicio, tokens) = Construir();

        await servicio.TokenizarAsync(Valida(guardar: true), Ana, CancellationToken.None);
        await servicio.TokenizarAsync(Valida(guardar: true), clienteId: null, CancellationToken.None);

        Assert.True(tokens.Todos[0].GuardarAlCobrar);
        Assert.False(tokens.Todos[1].GuardarAlCobrar);
        Assert.Null(tokens.Todos[1].ClienteId);
    }

    [Fact]
    public async Task El_worker_marca_EXPIRADO_y_borra_los_datos_solo_de_los_tokens_vencidos()
    {
        var momento = Ahora;
        var (servicio, tokens) = Construir(() => momento);
        await servicio.TokenizarAsync(Valida(), Ana, CancellationToken.None);
        momento = Ahora.AddMinutes(20);
        await servicio.TokenizarAsync(Valida(), Ana, CancellationToken.None);

        // A los 31 minutos solo el primero (creado en Ahora) vencio.
        momento = Ahora.AddMinutes(31);
        var expirados = await servicio.ExpirarVencidosAsync(CancellationToken.None);

        Assert.Equal(1, expirados);
        Assert.Equal(EstadoTokenPago.EXPIRADO, tokens.Todos[0].Estado);
        Assert.Null(tokens.Todos[0].DatosCifrados);
        Assert.Null(tokens.Todos[0].Nonce);
        Assert.Null(tokens.Todos[0].Tag);
        Assert.Equal(EstadoTokenPago.ACTIVO, tokens.Todos[1].Estado);
        Assert.NotNull(tokens.Todos[1].DatosCifrados);
    }

    [Fact]
    public void Expirar_no_toca_un_token_ya_consumido()
    {
        var token = BovedaPruebas.TokenNuevo("tok_consumido");
        token.Consumir();

        token.Expirar();

        Assert.Equal(EstadoTokenPago.CONSUMIDO, token.Estado);
    }

    // ── Controlador: invitados si, guardar solo con sesion ─────────────────────

    private static TokensController Controlador(ServicioTokensPago servicio, string? email)
    {
        var identidad = email is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }, "Bearer");

        return new TokensController(servicio)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identidad) }
            }
        };
    }

    [Fact]
    public async Task POST_tokens_como_invitado_responde_201_y_el_token_no_tiene_dueno()
    {
        var (servicio, tokens) = Construir();

        var respuesta = await Controlador(servicio, email: null).Crear(Valida(guardar: true), CancellationToken.None);

        var objeto = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status201Created, objeto.StatusCode);
        Assert.IsType<TokenPagoDto>(objeto.Value);
        var token = Assert.Single(tokens.Todos);
        Assert.Null(token.ClienteId);
        Assert.False(token.GuardarAlCobrar);
    }

    [Fact]
    public async Task POST_tokens_con_tarjeta_invalida_responde_422_con_errores_por_campo()
    {
        var (servicio, _) = Construir();

        var respuesta = await Controlador(servicio, Ana).Crear(
            new SolicitudTokenPago("4242424242424242", 1, 2020, "731", "Ana Lopez"), CancellationToken.None);

        var objeto = Assert.IsType<UnprocessableEntityObjectResult>(respuesta);
        var errores = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            objeto.Value!.GetType().GetProperty("errores")!.GetValue(objeto.Value));
        Assert.Equal("La tarjeta esta vencida.", errores["anioExp"]);
    }

    [Fact]
    public void La_solicitud_no_imprime_el_numero_ni_el_CVV()
    {
        var texto = Valida().ToString();

        Assert.DoesNotContain("4242", texto);
        Assert.DoesNotContain("731", texto);
    }
}
