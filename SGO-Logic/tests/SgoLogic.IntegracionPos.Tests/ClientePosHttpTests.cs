using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Options;
using SgoLogic.IntegracionPos.Worker.Infrastructure.Pos;

namespace SgoLogic.IntegracionPos.Tests;

public sealed class ClientePosHttpTests
{
    [Fact]
    public async Task Devuelve_fallo_despues_de_agotar_reintentos_para_503()
    {
        var handler = new RespuestaFijaHandler(HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://pos.test") };
        var opciones = Options.Create(new PosOptions { MaxReintentos = 2, TiempoEsperaMs = 1000 });
        var cliente = new ClientePosHttp(http, opciones);

        var respuesta = await cliente.EnviarAsync("TDA-014", "T14", "evt-1", "ord-1", "OrdenConfirmada", 10m);

        Assert.False(respuesta.Aceptado);
        Assert.False(respuesta.Duplicado);
        Assert.Equal(3, handler.TotalSolicitudes);
        Assert.Equal(503, respuesta.CodigoHttp);
    }

    [Fact]
    public async Task Trata_DUPLICADO_como_resultado_idempotente_exitoso()
    {
        var handler = new RespuestaFijaHandler(HttpStatusCode.OK, "{\"estado\":\"DUPLICADO\",\"mensaje\":\"ya procesado\"}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://pos.test") };
        var opciones = Options.Create(new PosOptions { MaxReintentos = 0, TiempoEsperaMs = 1000 });
        var cliente = new ClientePosHttp(http, opciones);

        var respuesta = await cliente.EnviarAsync("TDA-014", "T14", "evt-1", "ord-1", "OrdenConfirmada", 10m);

        Assert.False(respuesta.Aceptado);
        Assert.True(respuesta.Duplicado);
        Assert.Equal("DUPLICADO", respuesta.Estado);
    }

    private sealed class RespuestaFijaHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public RespuestaFijaHandler(HttpStatusCode statusCode, string? body = null)
        {
            _statusCode = statusCode;
            _body = body ?? "{\"estado\":\"NO_DISPONIBLE\"}";
        }

        public int TotalSolicitudes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TotalSolicitudes++;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            });
        }
    }
}
