using System.Net;
using System.Net.Http;
using System.Text.Json;
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

    [Fact]
    public async Task Obtiene_historial_de_clientes_desde_erp()
    {
        var json = "{\"fechaCorte\":\"2026-10-02T06:00:00.000Z\",\"total\":1,\"clientes\":[{\"codigoCliente\":\"ERP-CLI-0001\",\"email\":\"cliente1@sgo.local\",\"clienteDesde\":\"2023-09-28T00:00:00.000Z\",\"comprasUltimos12Meses\":18,\"montoCompradoUltimos12Meses\":14500.00,\"moneda\":\"GTQ\",\"pagosPuntuales\":18,\"pagosTardios\":0,\"maxDiasAtraso\":0,\"devoluciones\":1,\"incidentesGraves\":0,\"otrosIncidentes\":0,\"actualizadoEn\":\"2026-10-02T06:00:00.000Z\"}]}";
        var handler = new FijoHandler(HttpStatusCode.OK, json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var cliente = new ClienteErpHttp(http, Options.Create(new ErpOptions { MaxReintentos = 0 }));

        var historial = await cliente.ObtenerHistorialClientesAsync(modificadosDesde: null);

        Assert.Single(historial);
        Assert.Equal("cliente1@sgo.local", historial[0].Email);
        Assert.Equal("ERP-CLI-0001", historial[0].CodigoCliente);
        Assert.Equal(18, historial[0].ComprasUltimos12Meses);
    }

    [Fact]
    public async Task Envia_pagos_credito_al_endpoint_batch_del_erp()
    {
        var handler = new FijoHandler(HttpStatusCode.OK, "{\"estado\":\"ACEPTADO\"}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var cliente = new ClienteErpHttp(http, Options.Create(new ErpOptions { MaxReintentos = 0 }));

        var respuesta = await cliente.EnviarPagosCreditoAsync([
            new PagoCreditoErpRequest("event-1", "cuenta-1", "orden-1", "cliente@sgo.local", DateTime.UnixEpoch, Puntual: false)
        ]);

        Assert.True(respuesta.Exitoso);
        Assert.Equal(HttpMethod.Post, handler.Metodo);
        Assert.Equal("/erp/clientes/pagos", handler.Uri?.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Cuerpo!);
        var pago = body.RootElement.GetProperty("pagos")[0];
        Assert.Equal("event-1", pago.GetProperty("eventId").GetString());
        Assert.Equal("cliente@sgo.local", pago.GetProperty("clienteId").GetString());
        Assert.False(pago.GetProperty("puntual").GetBoolean());
    }

    private sealed class FijoHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public FijoHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        public int Solicitudes { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Cuerpo { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Solicitudes++;
            Metodo = request.Method;
            Uri = request.RequestUri;
            Cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
