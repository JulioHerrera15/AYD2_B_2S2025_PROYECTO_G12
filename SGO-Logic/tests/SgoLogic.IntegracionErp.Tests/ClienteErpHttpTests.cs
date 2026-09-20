using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Options;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Tests;

public sealed class ClienteErpHttpTests
{
    [Fact]
    public async Task Reintenta_503_y_devuelve_fallo_al_agotar_intentos()
    {
        var handler = new FijoHandler(HttpStatusCode.ServiceUnavailable, "{\"estado\":\"NO_DISPONIBLE\"}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var cliente = new ClienteErpHttp(http, Options.Create(new ErpOptions { MaxReintentos = 2 }));

        var respuesta = await cliente.EnviarLoteAsync("lote", "text/csv");

        Assert.False(respuesta.Exitoso);
        Assert.Equal(3, handler.Solicitudes);
    }

    [Fact]
    public async Task Acepta_409_DUPLICADO_como_idempotencia()
    {
        var handler = new FijoHandler(HttpStatusCode.Conflict, "{\"estado\":\"DUPLICADO\"}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var cliente = new ClienteErpHttp(http, Options.Create(new ErpOptions { MaxReintentos = 0 }));

        var respuesta = await cliente.EnviarLoteAsync("lote", "text/csv");

        Assert.True(respuesta.Exitoso);
        Assert.True(respuesta.Duplicado);
    }

    private sealed class FijoHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public FijoHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        public int Solicitudes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Solicitudes++;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }
}
