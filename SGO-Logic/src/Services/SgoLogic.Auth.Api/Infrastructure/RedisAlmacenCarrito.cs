using System.Text.Json;
using SgoLogic.Auth.Api.Application;
using StackExchange.Redis;

namespace SgoLogic.Auth.Api.Infrastructure;

/// <summary>
/// <see cref="IAlmacenCarrito" /> sobre Redis: el carrito es un string JSON con la
/// lista de items y TTL. Mismo formato que se usaba antes de separar el almacen,
/// asi que los carritos ya guardados se siguen leyendo.
/// </summary>
public sealed class RedisAlmacenCarrito : IAlmacenCarrito
{
    private readonly IDatabase _redis;

    public RedisAlmacenCarrito(IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
    }

    public async Task<List<ItemCarritoDto>?> LeerAsync(string clave)
    {
        var json = await _redis.StringGetAsync(clave);
        if (json.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<ItemCarritoDto>>(json.ToString()) ?? new List<ItemCarritoDto>();
    }

    public Task GuardarAsync(string clave, IReadOnlyList<ItemCarritoDto> items, TimeSpan ttl) =>
        _redis.StringSetAsync(clave, JsonSerializer.Serialize(items), ttl);

    public Task BorrarAsync(string clave) => _redis.KeyDeleteAsync(clave);
}
