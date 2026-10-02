using System.Net;
using System.Text;
using System.Text.Json;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>
/// Pruebas del cliente HTTP real de PayKlik contra un manejador falso: el cuerpo del
/// cargo con los datos de la tarjeta (A4) y como se clasifica cada respuesta. La
/// clasificacion es lo que separa un medio de pago rechazado (error del cliente) de
/// un rechazo por fondos (respuesta de negocio) y de una caida del proveedor.
/// </summary>
public sealed class ClientePayKlikHttpTests
{
    private static PeticionCargoPayKlik Peticion() => new(
        "ORD-2026-000001", "1400.00", Moneda.GTQ, MedioPago.TARJETA,
        Tarjeta: new TarjetaCargoPayKlik("4242424242424242", 12, 2030, "731", "Ana Lopez"),
        Guardar: true);

    private static ClientePayKlikHttp Cliente(ManejadorHttpFalso manejador) =>
        new(new HttpClient(manejador) { BaseAddress = new Uri("http://payklik.test") });

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo, string tipo = "application/json") =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, tipo) };

    private static HttpResponseMessage Problema(string? codigo, HttpStatusCode estado = HttpStatusCode.UnprocessableEntity) => Json(
        estado,
        JsonSerializer.Serialize(codigo is null
            ? new Dictionary<string, object?> { ["title"] = "Error", ["status"] = (int)estado }
            : new Dictionary<string, object?> { ["title"] = "Error", ["status"] = (int)estado, ["codigo"] = codigo, ["detail"] = "Detalle de PayKlik." }),
        "application/problem+json");

    [Fact]
    public async Task El_cargo_envia_la_tarjeta_en_el_cuerpo_y_la_clave_de_idempotencia_y_lee_la_referencia_guardada()
    {
        var aprobado = new ResultadoPayKlik(
            "PK-AUTH-1", EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado", "4242", "VISA", DateTime.UtcNow,
            ReferenciaTarjeta: "card_pk_nueva");
        var manejador = new ManejadorHttpFalso(_ => Json(
            HttpStatusCode.OK, JsonSerializer.Serialize(aprobado, SgoJsonSerializerOptions.Default)));

        var resultado = await Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None);

        var solicitud = Assert.Single(manejador.Solicitudes);
        Assert.Equal("POST", solicitud.Metodo);
        Assert.Equal("/v1/cargos", solicitud.Ruta);
        Assert.Equal("pago-123", solicitud.ClaveIdempotencia);

        using var cuerpo = JsonDocument.Parse(solicitud.Cuerpo);
        var raiz = cuerpo.RootElement;
        Assert.Equal("ORD-2026-000001", raiz.GetProperty("referencia").GetString());
        Assert.Equal("1400.00", raiz.GetProperty("monto").GetString());
        Assert.Equal("GTQ", raiz.GetProperty("moneda").GetString());
        Assert.Equal("TARJETA", raiz.GetProperty("medioPago").GetString());
        Assert.True(raiz.GetProperty("guardar").GetBoolean());

        var tarjeta = raiz.GetProperty("tarjeta");
        Assert.Equal("4242424242424242", tarjeta.GetProperty("numero").GetString());
        Assert.Equal(12, tarjeta.GetProperty("mesExp").GetInt32());
        Assert.Equal(2030, tarjeta.GetProperty("anioExp").GetInt32());
        Assert.Equal("731", tarjeta.GetProperty("cvv").GetString());
        Assert.Equal("Ana Lopez", tarjeta.GetProperty("titular").GetString());

        Assert.Equal("4242", resultado.UltimosCuatro);
        Assert.Equal("VISA", resultado.Marca);
        Assert.Equal("card_pk_nueva", resultado.ReferenciaTarjeta);
    }

    [Fact]
    public async Task El_cargo_con_tarjeta_guardada_envia_la_referencia_y_el_cvv_sin_numero()
    {
        var aprobado = new ResultadoPayKlik("PK-AUTH-2", EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado", "1111", "VISA", DateTime.UtcNow);
        var manejador = new ManejadorHttpFalso(_ => Json(
            HttpStatusCode.OK, JsonSerializer.Serialize(aprobado, SgoJsonSerializerOptions.Default)));
        var peticion = new PeticionCargoPayKlik(
            "ORD-2026-000002", "50.00", Moneda.GTQ, MedioPago.TARJETA, ReferenciaTarjeta: "card_pk_x", Cvv: "123");

        await Cliente(manejador).CargarAsync(peticion, "pago-456", CancellationToken.None);

        using var cuerpo = JsonDocument.Parse(Assert.Single(manejador.Solicitudes).Cuerpo);
        Assert.Equal("card_pk_x", cuerpo.RootElement.GetProperty("referenciaTarjeta").GetString());
        Assert.Equal("123", cuerpo.RootElement.GetProperty("cvv").GetString());
        Assert.Equal(JsonValueKind.Null, cuerpo.RootElement.GetProperty("tarjeta").ValueKind);
    }

    [Theory]
    [InlineData(CodigosToken.TarjetaInvalida)]
    [InlineData(CodigosToken.CvvInvalido)]
    [InlineData(CodigosToken.TarjetaVencida)]
    public async Task Un_422_con_codigo_de_medio_de_pago_se_lanza_como_medio_de_pago_rechazado(string codigo)
    {
        var manejador = new ManejadorHttpFalso(_ => Problema(codigo));

        var ex = await Assert.ThrowsAsync<TokenMedioPagoInvalidoException>(
            () => Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None));

        Assert.Equal(codigo, ex.Codigo);
        Assert.Equal("Detalle de PayKlik.", ex.Message);
    }

    [Fact]
    public async Task Un_404_de_referencia_inexistente_se_lanza_con_su_codigo()
    {
        var manejador = new ManejadorHttpFalso(_ => Problema(CodigosToken.TarjetaNoEncontrada, HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<TokenMedioPagoInvalidoException>(
            () => Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None));

        Assert.Equal(CodigosToken.TarjetaNoEncontrada, ex.Codigo);
    }

    [Fact]
    public void La_peticion_de_cargo_no_imprime_los_datos_de_la_tarjeta()
    {
        var impresa = Peticion().ToString();

        Assert.DoesNotContain("4242424242424242", impresa);
        Assert.DoesNotContain("731", impresa);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("OTRO_ERROR")]
    [InlineData(CodigosToken.Invalido)]
    public async Task Un_422_sin_codigo_de_medio_de_pago_no_se_confunde_con_un_medio_rechazado(string? codigo)
    {
        var manejador = new ManejadorHttpFalso(_ => Problema(codigo));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None));
    }

    [Fact]
    public async Task Un_422_con_cuerpo_que_no_es_JSON_no_se_confunde_con_un_token_no_valido()
    {
        var manejador = new ManejadorHttpFalso(_ => Json(HttpStatusCode.UnprocessableEntity, "no es json", "text/plain"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None));
    }

    [Fact]
    public async Task Un_503_es_proveedor_no_disponible_y_no_token_no_valido()
    {
        var manejador = new ManejadorHttpFalso(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<PayKlikNoDisponibleException>(
            () => Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None));
    }

    [Fact]
    public async Task Un_rechazo_por_fondos_es_una_respuesta_normal_no_una_excepcion()
    {
        var rechazado = new ResultadoPayKlik(
            null, EstadoRespuestaPayKlik.RECHAZADO, "51", "Fondos insuficientes", "4242", "VISA", DateTime.UtcNow);
        var manejador = new ManejadorHttpFalso(_ => Json(
            HttpStatusCode.OK, JsonSerializer.Serialize(rechazado, SgoJsonSerializerOptions.Default)));

        var resultado = await Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None);

        Assert.Equal(EstadoRespuestaPayKlik.RECHAZADO, resultado.Estado);
        Assert.Equal("51", resultado.CodigoRespuesta);
    }
}
