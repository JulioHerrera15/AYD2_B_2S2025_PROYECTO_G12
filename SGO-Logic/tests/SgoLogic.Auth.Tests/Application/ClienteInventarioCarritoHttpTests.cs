using System.Net;
using System.Text;
using SgoLogic.Auth.Api.Application;
using SgoLogic.Auth.Api.Infrastructure.Inventario;
using SgoLogic.Auth.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Auth.Tests.Application;

/// <summary>Clasificacion de las respuestas de Inventario en el cliente HTTP del carrito.</summary>
public sealed class ClienteInventarioCarritoHttpTests
{
    private const string Carrito = "cart:a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static ClienteInventarioCarritoHttp Cliente(ManejadorHttpFalso manejador) =>
        new(new HttpClient(manejador) { BaseAddress = new Uri("http://inventario-api:8080") });

    private static Func<HttpRequestMessage, HttpResponseMessage> Responder(HttpStatusCode status, string cuerpo) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sin_conexion_a_Inventario_lanza_no_disponible()
    {
        var cliente = Cliente(ManejadorHttpFalso.Caido());

        await Assert.ThrowsAsync<ExcepcionInventarioNoDisponible>(
            () => cliente.RetenerAsync(Carrito, "PROD-101", 1, "TIENDA-01-CENTRAL"));
    }

    [Fact]
    public async Task Un_409_sube_como_rechazo_con_el_mismo_cuerpo()
    {
        const string cuerpo = "{\"sku\":\"PROD-104\",\"disponible\":0,\"mensaje\":\"No hay existencias de PROD-104 en ninguna sucursal.\",\"sucursalCercana\":null}";
        var cliente = Cliente(new ManejadorHttpFalso(Responder(HttpStatusCode.Conflict, cuerpo)));

        var ex = await Assert.ThrowsAsync<ExcepcionInventarioRechazo>(
            () => cliente.RetenerAsync(Carrito, "PROD-104", 1, "TIENDA-01-CENTRAL"));

        Assert.Equal(409, ex.Status);
        Assert.Equal(cuerpo, ex.Cuerpo);
    }

    [Fact]
    public async Task Un_5xx_es_Inventario_no_disponible()
    {
        var cliente = Cliente(new ManejadorHttpFalso(Responder(HttpStatusCode.InternalServerError, "{}")));

        await Assert.ThrowsAsync<ExcepcionInventarioNoDisponible>(
            () => cliente.LiberarItemAsync(Carrito, "PROD-101"));
    }

    [Fact]
    public async Task Retener_hace_PUT_al_item_del_carrito_y_lee_la_linea()
    {
        const string cuerpo = "{\"sku\":\"PROD-102\",\"ubicacionId\":\"TIENDA-05-OAKLAND\",\"nombreUbicacion\":\"MegaStore Oakland Mall\"," +
                              "\"tipoUbicacion\":\"TIENDA\",\"cantidad\":2,\"expiraEn\":\"2026-10-01T17:15:00.000Z\"," +
                              "\"estado\":\"ACTIVA\",\"enOtraSucursal\":true,\"sucursalSugerida\":null}";
        var manejador = new ManejadorHttpFalso(Responder(HttpStatusCode.OK, cuerpo));

        var linea = await Cliente(manejador).RetenerAsync(Carrito, "PROD-102", 2, "TIENDA-01-CENTRAL");

        var solicitud = Assert.Single(manejador.Solicitudes);
        Assert.Equal(HttpMethod.Put, solicitud.Method);
        Assert.EndsWith("/items/PROD-102", solicitud.RequestUri!.AbsolutePath);
        Assert.Equal("TIENDA-05-OAKLAND", linea.UbicacionId);
        Assert.True(linea.EnOtraSucursal);
        Assert.Equal(new DateTime(2026, 10, 1, 17, 15, 0, DateTimeKind.Utc), linea.ExpiraEn);
    }
}
