using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Controllers;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Application;

public sealed class CreditoControllerTests
{
    private readonly AlmacenCreditoFalso _almacen = new();

    private CreditoController Controlador(string? email, string query = "")
    {
        var claims = email is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.Email, email) };
        var contexto = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
        contexto.Request.QueryString = new QueryString(query);

        return new CreditoController(DatosPrueba.ServicioElegibilidad(_almacen))
        {
            ControllerContext = new ControllerContext { HttpContext = contexto }
        };
    }

    [Fact]
    public async Task Toma_el_cliente_del_claim_e_ignora_el_clienteId_de_la_query()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, new OutboxWriterFalso());
        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None);
        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente4(), CancellationToken.None);

        var controlador = Controlador("Cliente4@SGO.local", "?clienteId=cliente1@sgo.local&monto=100");

        var resultado = await controlador.Elegibilidad(100m, CancellationToken.None);

        var oferta = Assert.IsType<OfertaCreditoDto>(Assert.IsType<OkObjectResult>(resultado).Value);
        Assert.Equal("cliente4@sgo.local", _almacen.UltimoClienteConsultado);
        Assert.Equal(EstadosOfertaCredito.NoElegible, oferta.Estado);
    }

    [Fact]
    public async Task Mi_credito_tambien_usa_el_claim()
    {
        var controlador = Controlador("cliente2@sgo.local", "?clienteId=cliente1@sgo.local");

        var resultado = await controlador.MiCredito(CancellationToken.None);

        Assert.IsType<MiCreditoDto>(Assert.IsType<OkObjectResult>(resultado).Value);
        Assert.Equal("cliente2@sgo.local", _almacen.UltimoClienteConsultado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Monto_cero_o_negativo_responde_400_sin_consultar(decimal monto)
    {
        var controlador = Controlador("cliente1@sgo.local");

        var resultado = await controlador.Elegibilidad(monto, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(resultado).StatusCode);
        Assert.Equal(0, _almacen.Consultas);
    }

    [Fact]
    public async Task Sin_claim_de_email_responde_401()
    {
        var controlador = Controlador(null, "?clienteId=cliente1@sgo.local");

        var resultado = await controlador.Elegibilidad(null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(resultado).StatusCode);
        Assert.Equal(0, _almacen.Consultas);
    }
}
