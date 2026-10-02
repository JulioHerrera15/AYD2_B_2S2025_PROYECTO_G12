using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Api.Infrastructure.Inventario;

/// <summary>
/// Cliente HTTP de GET /api/inventario/productos/{sku} contra Inventario:BaseUrl (red
/// interna, sin pasar por el Gateway). Cada SKU se cachea <see cref="Vigencia" /> en
/// memoria, tambien el "no existe": bajo carga, crear muchas ordenes del mismo SKU no
/// multiplica las llamadas, y un cambio de precio se ve en segundos.
///
/// Sin respuesta, timeout o 5xx lanza <see cref="ExcepcionCatalogoNoDisponible" /> y
/// no cachea nada.
/// </summary>
public sealed class CatalogoProductosHttp : ICatalogoProductos
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public CatalogoProductosHttp(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<ProductoCatalogo?> ObtenerAsync(string sku, CancellationToken cancelacion)
    {
        var clave = "catalogo:" + sku.Trim().ToUpperInvariant();
        if (_cache.TryGetValue(clave, out EntradaCatalogo? entrada) && entrada is not null)
        {
            return entrada.Producto;
        }

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.GetAsync($"/api/inventario/productos/{Uri.EscapeDataString(sku.Trim())}", cancelacion);
        }
        catch (HttpRequestException ex)
        {
            throw new ExcepcionCatalogoNoDisponible("Inventario no respondio al validar los precios.", ex);
        }
        catch (TaskCanceledException ex) when (!cancelacion.IsCancellationRequested)
        {
            throw new ExcepcionCatalogoNoDisponible("Inventario no respondio a tiempo al validar los precios.", ex);
        }

        using (respuesta)
        {
            ProductoCatalogo? producto;
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                producto = null;
            }
            else if (respuesta.IsSuccessStatusCode)
            {
                try
                {
                    producto = await respuesta.Content.ReadFromJsonAsync<ProductoCatalogo>(SgoJsonSerializerOptions.Default, cancelacion)
                        ?? throw new ExcepcionCatalogoNoDisponible("Inventario respondio un producto vacio.");
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new ExcepcionCatalogoNoDisponible("Inventario respondio un producto ilegible.", ex);
                }
            }
            else
            {
                throw new ExcepcionCatalogoNoDisponible(
                    $"Inventario respondio {(int)respuesta.StatusCode} al validar los precios.");
            }

            _cache.Set(clave, new EntradaCatalogo(producto), Vigencia);
            return producto;
        }
    }

    private sealed record EntradaCatalogo(ProductoCatalogo? Producto);
}
