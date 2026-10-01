namespace SgoLogic.Auth.Api.Application;

/// <summary>
/// Donde vive el contenido del carrito: una lista de items por clave (cart:{usuarioId}
/// o guest:{sesionId}) con TTL. La implementacion es Redis; separarla permite probar
/// <see cref="CarritoService" /> con un doble en memoria.
/// </summary>
public interface IAlmacenCarrito
{
    /// <summary>Items guardados bajo la clave, o null si la clave no existe.</summary>
    Task<List<ItemCarritoDto>?> LeerAsync(string clave);

    Task GuardarAsync(string clave, IReadOnlyList<ItemCarritoDto> items, TimeSpan ttl);

    Task BorrarAsync(string clave);
}
