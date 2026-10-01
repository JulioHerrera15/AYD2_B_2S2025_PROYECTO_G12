using SgoLogic.Auth.Api.Application;

namespace SgoLogic.Auth.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IAlmacenCarrito" /> (Redis). Cuenta escrituras y borrados.</summary>
public sealed class AlmacenCarritoFalso : IAlmacenCarrito
{
    private readonly Dictionary<string, List<ItemCarritoDto>> _carritos = new();

    public int Escrituras { get; private set; }

    public int Borrados { get; private set; }

    public AlmacenCarritoFalso Precargar(string clave, params ItemCarritoDto[] items)
    {
        _carritos[clave] = items.ToList();
        return this;
    }

    public IReadOnlyList<ItemCarritoDto>? Contenido(string clave) =>
        _carritos.TryGetValue(clave, out var items) ? items : null;

    public Task<List<ItemCarritoDto>?> LeerAsync(string clave) =>
        Task.FromResult(_carritos.TryGetValue(clave, out var items) ? items.ToList() : null);

    public Task GuardarAsync(string clave, IReadOnlyList<ItemCarritoDto> items, TimeSpan ttl)
    {
        Escrituras++;
        _carritos[clave] = items.ToList();
        return Task.CompletedTask;
    }

    public Task BorrarAsync(string clave)
    {
        Borrados++;
        _carritos.Remove(clave);
        return Task.CompletedTask;
    }
}
