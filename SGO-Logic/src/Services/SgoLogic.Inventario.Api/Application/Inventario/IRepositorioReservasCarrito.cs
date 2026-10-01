using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>Puerto de persistencia de <see cref="ReservaCarrito" />. No llama a SaveChanges.</summary>
public interface IRepositorioReservasCarrito
{
    /// <summary>
    /// Retenciones vigentes (ACTIVA o EXPIRADA) de un carrito, con seguimiento para
    /// escritura. Hay a lo sumo una por SKU.
    /// </summary>
    Task<IReadOnlyList<ReservaCarrito>> ListarVigentesPorCarritoAsync(string carritoId, CancellationToken cancelacion);

    /// <summary>
    /// Retenciones ACTIVA cuyo TTL ya vencio en <paramref name="ahora" />, de la mas
    /// vencida a la mas reciente y a lo sumo <paramref name="limite" />. Se apoya en
    /// el indice ix_reserva_carrito_estado_expira_en.
    /// </summary>
    Task<IReadOnlyList<ReservaCarrito>> ListarActivasVencidasAsync(DateTime ahora, int limite, CancellationToken cancelacion);

    Task<ReservaCarrito?> ObtenerPorIdAsync(Guid id, CancellationToken cancelacion);

    Task AgregarAsync(ReservaCarrito retencion, CancellationToken cancelacion);
}
