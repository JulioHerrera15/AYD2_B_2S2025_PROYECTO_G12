using System.Text.Json;
using StackExchange.Redis;

namespace SgoLogic.Auth.Api.Application;

public interface ICarritoService
{
    Task<CarritoDto> ObtenerAsync(string clienteOSesionId, CancellationToken ct = default);
    Task AgregarItemAsync(string clienteOSesionId, AgregarItemRequest request, CancellationToken ct = default);
    Task EliminarItemAsync(string clienteOSesionId, string sku, CancellationToken ct = default);
    Task AsociarAsync(string sesionId, string clienteId, CancellationToken ct = default);
}

public sealed class CarritoService : ICarritoService
{
    private readonly IDatabase _redis;
    private readonly int _ttlSegundos;

    // Prefijos de clave Redis
    private const string PrefijoCuenta = "cart:";
    private const string PrefijoInvitado = "guest:";

    public CarritoService(IConnectionMultiplexer redis, IConfiguration config)
    {
        _redis = redis.GetDatabase();
        _ttlSegundos = config.GetValue<int>("Auth:CarritoTtlSegundos", 86_400);
    }

    public async Task<CarritoDto> ObtenerAsync(string clienteOSesionId, CancellationToken ct = default)
    {
        var clave = ClavePara(clienteOSesionId);
        var json = await _redis.StringGetAsync(clave);

        if (json.IsNullOrEmpty)
            return new CarritoDto(clienteOSesionId, [], 0m);

        var items = JsonSerializer.Deserialize<List<ItemCarritoDto>>(json.ToString()) ?? [];
        var total = items.Sum(i => i.PrecioUnitario * i.Cantidad);
        return new CarritoDto(clienteOSesionId, items, total);
    }

    public async Task AgregarItemAsync(string clienteOSesionId, AgregarItemRequest request, CancellationToken ct = default)
    {
        if (request.Cantidad <= 0)
            throw new ArgumentException("La cantidad debe ser mayor que cero.");
        if (request.PrecioUnitario < 0)
            throw new ArgumentException("El precio unitario no puede ser negativo.");

        var clave = ClavePara(clienteOSesionId);
        var json = await _redis.StringGetAsync(clave);
        var items = json.IsNullOrEmpty
            ? new List<ItemCarritoDto>()
            : JsonSerializer.Deserialize<List<ItemCarritoDto>>(json.ToString()) ?? new List<ItemCarritoDto>();

        var existente = items.FirstOrDefault(i => i.Sku == request.Sku);
        if (existente is not null)
        {
            // Sumar cantidad si el SKU ya esta en el carrito
            items.Remove(existente);
            items.Add(existente with { Cantidad = existente.Cantidad + request.Cantidad });
        }
        else
        {
            items.Add(new ItemCarritoDto(request.Sku, request.Descripcion, request.Cantidad, request.PrecioUnitario));
        }

        await _redis.StringSetAsync(clave, JsonSerializer.Serialize(items), TimeSpan.FromSeconds(_ttlSegundos));
    }

    public async Task EliminarItemAsync(string clienteOSesionId, string sku, CancellationToken ct = default)
    {
        var clave = ClavePara(clienteOSesionId);
        var json = await _redis.StringGetAsync(clave);

        if (json.IsNullOrEmpty) return;

        var items = JsonSerializer.Deserialize<List<ItemCarritoDto>>(json.ToString()) ?? new List<ItemCarritoDto>();
        items.RemoveAll(i => i.Sku == sku);

        if (items.Count == 0)
            await _redis.KeyDeleteAsync(clave);
        else
            await _redis.StringSetAsync(clave, JsonSerializer.Serialize(items), TimeSpan.FromSeconds(_ttlSegundos));
    }

    public async Task AsociarAsync(string sesionId, string clienteId, CancellationToken ct = default)
    {
        // Migra guest:{sesionId} → cart:{clienteId}
        // Si cart:{clienteId} ya existe, mergea (la cuenta gana en duplicados de SKU)
        var claveInvitado = PrefijoInvitado + sesionId;
        var claveCuenta = PrefijoCuenta + clienteId;

        var jsonInvitado = await _redis.StringGetAsync(claveInvitado);
        if (jsonInvitado.IsNullOrEmpty) return; // no hay carrito de invitado, nada que hacer

        var itemsInvitado = JsonSerializer.Deserialize<List<ItemCarritoDto>>(jsonInvitado.ToString()) ?? [];

        var jsonCuenta = await _redis.StringGetAsync(claveCuenta);
        var itemsCuenta = jsonCuenta.IsNullOrEmpty
            ? new List<ItemCarritoDto>()
            : JsonSerializer.Deserialize<List<ItemCarritoDto>>(jsonCuenta.ToString()) ?? [];

        // Mergear: si el SKU ya existe en la cuenta, se suman las cantidades;
        // si no existe, se agrega el item completo del invitado.
        foreach (var itemInvitado in itemsInvitado)
        {
            var existente = itemsCuenta.FirstOrDefault(i => i.Sku == itemInvitado.Sku);
            if (existente is not null)
            {
                itemsCuenta.Remove(existente);
                itemsCuenta.Add(existente with { Cantidad = existente.Cantidad + itemInvitado.Cantidad });
            }
            else
            {
                itemsCuenta.Add(itemInvitado);
            }
        }

        await _redis.StringSetAsync(claveCuenta, JsonSerializer.Serialize(itemsCuenta), TimeSpan.FromSeconds(_ttlSegundos));
        await _redis.KeyDeleteAsync(claveInvitado);
    }

    private static string ClavePara(string id)
    {
        // Si el id ya tiene prefijo "guest:" o "cart:" lo respeta; si no, asume cuenta
        return id.StartsWith(PrefijoInvitado) || id.StartsWith(PrefijoCuenta)
            ? id
            : PrefijoCuenta + id;
    }
}
