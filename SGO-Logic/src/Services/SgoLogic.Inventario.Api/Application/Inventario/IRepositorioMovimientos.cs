using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Puerto de persistencia del ledger <see cref="MovimientoExistencia" /> (HU-016).
/// Solo agrega y consulta: los asientos no se editan ni se borran.
/// </summary>
public interface IRepositorioMovimientos
{
    /// <summary>Registra un asiento nuevo en la unidad de trabajo.</summary>
    Task AgregarAsync(MovimientoExistencia movimiento, CancellationToken cancelacion);

    /// <summary>
    /// Historial de un SKU, del mas reciente al mas antiguo. Los filtros son
    /// opcionales: sin ubicacion trae todas, y las fechas acotan por
    /// <see cref="MovimientoExistencia.RegistradoEn" /> (<paramref name="desde" />
    /// inclusivo, <paramref name="hasta" /> inclusivo).
    /// </summary>
    Task<IReadOnlyList<MovimientoExistencia>> ListarAsync(
        string sku,
        string? ubicacionId,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancelacion);
}
