using System.Net;
using System.Net.Http.Json;
using SgoLogic.Auth.Api.Application;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.Auth.Api.Infrastructure.Inventario;

/// <summary>
/// Cliente HTTP tipado de <see cref="IClienteInventarioCarrito" /> contra
/// Inventario:BaseUrl (http://inventario-api:8080 en docker-compose). Los endpoints
/// /interno/ no tienen ruta en el Gateway: solo se alcanzan dentro de la red de
/// Docker Compose, igual que /interno/carrito de este mismo servicio.
///
/// A diferencia del vaciado de carrito que hace Ordenes (cortesia de UX), aqui la
/// falla NO se traga: si Inventario no confirma la retencion, el carrito no debe
/// cambiar, asi que la excepcion sube hasta el controlador (409 o 503).
/// </summary>
public sealed class ClienteInventarioCarritoHttp : IClienteInventarioCarrito
{
    private const string Base = "/interno/inventario/carritos";

    private readonly HttpClient _http;

    public ClienteInventarioCarritoHttp(HttpClient http)
    {
        _http = http;
    }

    public async Task<LineaRetencionInventario> RetenerAsync(
        string carritoId,
        string sku,
        int cantidad,
        string? ubicacionPreferidaId,
        CancellationToken ct = default)
    {
        using var respuesta = await EnviarAsync(
            () => _http.PutAsJsonAsync(
                $"{Base}/{Escapar(carritoId)}/items/{Escapar(sku)}",
                new { cantidad, ubicacionPreferidaId },
                SgoJsonSerializerOptions.Default,
                ct),
            ct);

        return await LeerAsync<LineaRetencionInventario>(respuesta, ct);
    }

    public async Task LiberarItemAsync(string carritoId, string sku, CancellationToken ct = default)
    {
        using var _ = await EnviarAsync(
            () => _http.DeleteAsync($"{Base}/{Escapar(carritoId)}/items/{Escapar(sku)}", ct),
            ct);
    }

    public async Task<RetencionesCarritoInventario> RenovarAsync(
        string carritoId,
        IReadOnlyList<string> skus,
        CancellationToken ct = default)
    {
        using var respuesta = await EnviarAsync(
            () => _http.PostAsJsonAsync($"{Base}/{Escapar(carritoId)}/renovar", new { skus }, SgoJsonSerializerOptions.Default, ct),
            ct);

        return await LeerAsync<RetencionesCarritoInventario>(respuesta, ct);
    }

    public async Task<RetencionesCarritoInventario> TransferirAsync(
        string carritoOrigenId,
        string carritoDestinoId,
        CancellationToken ct = default)
    {
        using var respuesta = await EnviarAsync(
            () => _http.PostAsJsonAsync(
                $"{Base}/{Escapar(carritoOrigenId)}/transferir",
                new { destinoCarritoId = carritoDestinoId },
                SgoJsonSerializerOptions.Default,
                ct),
            ct);

        return await LeerAsync<RetencionesCarritoInventario>(respuesta, ct);
    }

    public async Task<RetencionesCarritoInventario> ObtenerAsync(string carritoId, CancellationToken ct = default)
    {
        using var respuesta = await EnviarAsync(() => _http.GetAsync($"{Base}/{Escapar(carritoId)}", ct), ct);
        return await LeerAsync<RetencionesCarritoInventario>(respuesta, ct);
    }

    /// <summary>
    /// Ejecuta la llamada y clasifica el resultado: 2xx sigue, 409 sube con su cuerpo,
    /// 400 es un error de quien llamo, y todo lo demas (sin respuesta, timeout, 5xx)
    /// es Inventario no disponible.
    /// </summary>
    private static async Task<HttpResponseMessage> EnviarAsync(
        Func<Task<HttpResponseMessage>> llamada,
        CancellationToken ct)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await llamada();
        }
        catch (HttpRequestException ex)
        {
            throw new ExcepcionInventarioNoDisponible("Inventario no respondio.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExcepcionInventarioNoDisponible("Inventario no respondio a tiempo.", ex);
        }

        if (respuesta.IsSuccessStatusCode)
        {
            return respuesta;
        }

        using (respuesta)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            switch (respuesta.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    throw new ExcepcionInventarioRechazo(
                        StatusCodes.Status409Conflict,
                        cuerpo,
                        respuesta.Content.Headers.ContentType?.ToString());

                case HttpStatusCode.BadRequest:
                    throw new ArgumentException($"Inventario rechazo la solicitud del carrito: {cuerpo}");

                default:
                    throw new ExcepcionInventarioNoDisponible(
                        $"Inventario respondio {(int)respuesta.StatusCode} al operar el carrito.");
            }
        }
    }

    private static async Task<T> LeerAsync<T>(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            return await respuesta.Content.ReadFromJsonAsync<T>(SgoJsonSerializerOptions.Default, ct)
                ?? throw new ExcepcionInventarioNoDisponible("Inventario respondio sin cuerpo.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ExcepcionInventarioNoDisponible("Inventario respondio un cuerpo ilegible.", ex);
        }
    }

    private static string Escapar(string valor) => Uri.EscapeDataString(valor);
}
