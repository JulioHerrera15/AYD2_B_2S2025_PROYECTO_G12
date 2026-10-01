namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Puerto hacia el carrito de Auth. La implementacion vive en
/// Infrastructure/Carrito (llamada HTTP directa a auth-api, mismo patron que
/// Despacho usa para llamar a Inventario con <c>IClienteInventario</c>).
/// </summary>
public interface IClienteCarrito
{
    /// <summary>
    /// Vacia el carrito del cliente. Es una cortesia de UX, no una garantia de
    /// negocio: la implementacion nunca lanza por un fallo de Auth, para que
    /// confirmar una orden jamas dependa de que el carrito responda.
    /// </summary>
    Task VaciarAsync(string clienteId, CancellationToken cancelacion);
}