using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Carga y consulta de existencias por ubicacion. Es soporte de HU-001 (permite
/// que haya algo contra que reservar) y semilla de HU-016. No emite eventos.
/// </summary>
public sealed class ServicioExistencias
{
    private readonly IRepositorioExistencias _existencias;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;

    public ServicioExistencias(IRepositorioExistencias existencias, IUnidadDeTrabajo unidadDeTrabajo)
    {
        _existencias = existencias;
        _unidadDeTrabajo = unidadDeTrabajo;
    }

    /// <summary>Da de alta la existencia (SKU + ubicacion) o ajusta su cantidad fisica si ya existe.</summary>
    public async Task<ExistenciaDto> RegistrarAsync(RegistrarExistenciaRequest solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var existencia = await _existencias.ObtenerAsync(solicitud.Sku, solicitud.UbicacionId, cancelacion);

        if (existencia is null)
        {
            existencia = new Existencia(
                solicitud.Sku,
                solicitud.UbicacionId,
                solicitud.TipoUbicacion,
                solicitud.CantidadFisica);

            await _existencias.AgregarAsync(existencia, cancelacion);
        }
        else
        {
            existencia.AjustarCantidadFisica(solicitud.CantidadFisica);
        }

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return ExistenciaDto.Desde(existencia);
    }

    /// <summary>Lista las existencias de un SKU en todas sus ubicaciones.</summary>
    public async Task<IReadOnlyList<ExistenciaDto>> ListarPorSkuAsync(string sku, CancellationToken cancelacion)
    {
        var existencias = await _existencias.ListarPorSkuAsync(sku, cancelacion);
        return existencias.Select(ExistenciaDto.Desde).ToList();
    }
}
