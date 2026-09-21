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
/// cargo con token y como se clasifica cada respuesta. La clasificacion es lo que
/// separa un token invalido (error del cliente) de un rechazo por fondos (respuesta
/// de negocio) y de una caida del proveedor.
/// </summary>
public sealed class ClientePayKlikHttpTests
{
    private const string Token = "tok_pk_9f2a7c41d8e0b3a6c5d4e1f2";

    private static PeticionCargoPayKlik Peticion() => new(
        "ORD-2026-000001", "1400.00", Moneda.GTQ, MedioPago.TARJETA, Token);

    private static ClientePayKlikHttp Cliente(ManejadorHttpFalso manejador) =>
        new(new HttpClient(manejador) { BaseAddress = new Uri("http://payklik.test") });

    private static HttpResponseMessage Json(HttpStatusCode estado, string cuerpo, string tipo = "application/json") =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, tipo) };

    private static HttpResponseMessage Problema(string? codigo) => Json(
        HttpStatusCode.UnprocessableEntity,
        JsonSerializer.Serialize(codigo is null
            ? new Dictionary<string, object?> { ["title"] = "Error", ["status"] = 422 }
            : new Dictionary<string, object?> { ["title"] = "Error", ["status"] = 422, ["codigo"] = codigo, ["detail"] = "Detalle de PayKlik." }),
        "application/problem+json");

    [Fact]
    public async Task El_cargo_envia_el_token_en_el_cuerpo_y_la_clave_de_idempotencia_y_lee_la_tarjeta_real()
    {
        var aprobado = new ResultadoPayKlik(
            "PK-AUTH-1", EstadoRespuestaPayKlik.APROBADO, "00", "Cargo aprobado", "4242", "VISA", DateTime.UtcNow);
        var manejador = new ManejadorHttpFalso(_ => Json(
            HttpStatusCode.OK, JsonSerializer.Serialize(aprobado, SgoJsonSerializerOptions.Default)));

        var resultado = await Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None);

        var solicitud = Assert.Single(manejador.Solicitudes);
        Assert.Equal("POST", solicitud.Metodo);
        Assert.Equal("/v1/cargos", solicitud.Ruta);
        Assert.Equal("pago-123", solicitud.ClaveIdempotencia);

        // El cuerpo lleva exactamente estos cinco campos: el token y nada mas de la tarjeta.
        using var cuerpo = JsonDocument.Parse(solicitud.Cuerpo);
        var campos = cuerpo.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
        Assert.Equal(
            new[] { "referencia", "monto", "moneda", "medioPago", "tokenMedioPago" }.OrderBy(n => n),
            campos.Keys.OrderBy(n => n));
        Assert.Equal("ORD-2026-000001", campos["referencia"]);
        Assert.Equal("1400.00", campos["monto"]);
        Assert.Equal("GTQ", campos["moneda"]);
        Assert.Equal("TARJETA", campos["medioPago"]);
        Assert.Equal(Token, campos["tokenMedioPago"]);

        Assert.Equal("4242", resultado.UltimosCuatro);
        Assert.Equal("VISA", resultado.Marca);
    }

    [Theory]
    [InlineData(CodigosToken.Invalido)]
    [InlineData(CodigosToken.Vencido)]
    [InlineData(CodigosToken.YaUsado)]
    [InlineData(CodigosToken.Requerido)]
    public async Task Un_422_con_codigo_de_token_se_lanza_como_token_no_valido(string codigo)
    {
        var manejador = new ManejadorHttpFalso(_ => Problema(codigo));

        var ex = await Assert.ThrowsAsync<TokenMedioPagoInvalidoException>(
            () => Cliente(manejador).CargarAsync(Peticion(), "pago-123", CancellationToken.None));

        Assert.Equal(codigo, ex.Codigo);
        Assert.Equal("Detalle de PayKlik.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("OTRO_ERROR")]
    public async Task Un_422_sin_codigo_de_token_no_se_confunde_con_un_token_no_valido(string? codigo)
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
