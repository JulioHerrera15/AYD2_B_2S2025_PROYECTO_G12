using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tarjetas;

/// <summary>Puerto de persistencia de <see cref="TarjetaGuardada" />.</summary>
public interface IRepositorioTarjetasGuardadas
{
    /// <summary>Tarjetas activas del cliente, la de uso mas reciente primero.</summary>
    Task<IReadOnlyList<TarjetaGuardada>> ListarActivasAsync(string clienteId, CancellationToken cancelacion);

    /// <summary>Tarjeta activa por id, de cualquier cliente: quien llama verifica el dueno.</summary>
    Task<TarjetaGuardada?> ObtenerActivaAsync(Guid id, CancellationToken cancelacion);

    /// <summary>La tarjeta activa del cliente con la misma marca, ultimos cuatro y vencimiento, si existe.</summary>
    Task<TarjetaGuardada?> BuscarActivaIgualAsync(
        string clienteId,
        MarcaTarjeta marca,
        string ultimos4,
        int mesExp,
        int anioExp,
        CancellationToken cancelacion);

    Task AgregarAsync(TarjetaGuardada tarjeta, CancellationToken cancelacion);

    Task GuardarCambiosAsync(CancellationToken cancelacion);
}
