using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Carga y consulta de existencias por ubicacion. Es soporte de HU-001 (permite
/// que haya algo contra que reservar) y el nucleo de lectura de HU-016 y HU-017:
/// la vista unificada de las 32 tiendas y los 2 centros de distribucion. No emite
/// eventos; toda su salida es sincrona, leida de Postgres en el momento.
///
/// Todo cambio de cantidad fisica deja su asiento en el ledger de movimientos, en
/// la misma transaccion, para que el historial de entradas y salidas no pueda
/// divergir del saldo.
/// </summary>
public sealed class ServicioExistencias
{
    private readonly IRepositorioExistencias _existencias;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;

    public ServicioExistencias(
        IRepositorioExistencias existencias,
        IRepositorioMovimientos movimientos,
        IUnidadDeTrabajo unidadDeTrabajo)
    {
        _existencias = existencias;
        _movimientos = movimientos;
        _unidadDeTrabajo = unidadDeTrabajo;
    }

    /// <summary>
    /// Da de alta la existencia (SKU + ubicacion) o ajusta su cantidad fisica si ya
    /// existe. En ambos casos registra el movimiento que explica el cambio: el alta
    /// entra como CARGA_INICIAL y el ajuste como AJUSTE_MANUAL, con el sentido
    /// (ENTRADA o SALIDA) derivado del signo del delta. Ajustar al mismo valor no
    /// mueve nada y no deja asiento.
    /// </summary>
    public async Task<ExistenciaDto> RegistrarAsync(RegistrarExistenciaRequest solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var existencia = await _existencias.ObtenerAsync(solicitud.Sku, solicitud.UbicacionId, cancelacion);

        int delta;
        MotivoMovimiento motivo;

        if (existencia is null)
        {
            existencia = new Existencia(
                solicitud.Sku,
                solicitud.UbicacionId,
                solicitud.TipoUbicacion,
                solicitud.CantidadFisica);

            await _existencias.AgregarAsync(existencia, cancelacion);

            // El alta se asienta como ENTRADA para que el ledger quede completo desde
            // la primera unidad: sin ese asiento el historial arrancaria con un saldo
            // que nada explica.
            delta = solicitud.CantidadFisica;
            motivo = MotivoMovimiento.CARGA_INICIAL;
        }
        else
        {
            delta = solicitud.CantidadFisica - existencia.CantidadFisica;
            motivo = MotivoMovimiento.AJUSTE_MANUAL;

            existencia.AjustarCantidadFisica(solicitud.CantidadFisica);
        }

        if (delta != 0)
        {
            await _movimientos.AgregarAsync(
                new MovimientoExistencia(
                    existencia.Sku,
                    existencia.UbicacionId,
                    delta,
                    existencia.CantidadFisica,
                    motivo,
                    DateTime.UtcNow),
                cancelacion);
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

    /// <summary>
    /// Vista unificada: todas las existencias de todas las ubicaciones, paginada y
    /// con filtros opcionales por ubicacion y por tipo de ubicacion.
    /// </summary>
    public async Task<PaginaDto<ExistenciaDto>> ListarAsync(
        string? ubicacionId,
        TipoUbicacion? tipoUbicacion,
        int pagina,
        int tamano,
        CancellationToken cancelacion)
    {
        var paginaNormalizada = pagina < 1 ? PaginaPorDefecto : pagina;
        var tamanoNormalizado = tamano is < 1 or > TamanoMaximo ? TamanoPorDefecto : tamano;

        var (elementos, total) = await _existencias.ListarAsync(
            ubicacionId,
            tipoUbicacion,
            paginaNormalizada,
            tamanoNormalizado,
            cancelacion);

        return new PaginaDto<ExistenciaDto>(
            elementos.Select(ExistenciaDto.Desde).ToList(),
            paginaNormalizada,
            tamanoNormalizado,
            total);
    }

    /// <summary>Ubicaciones conocidas por Inventario, con el resumen de lo que guardan.</summary>
    public Task<IReadOnlyList<UbicacionResumenDto>> ListarUbicacionesAsync(CancellationToken cancelacion) =>
        _existencias.ResumirUbicacionesAsync(cancelacion);

    /// <summary>Que hay en una ubicacion concreta (la vista de una tienda o un centro de distribucion).</summary>
    public async Task<IReadOnlyList<ExistenciaDto>> ListarPorUbicacionAsync(
        string ubicacionId,
        CancellationToken cancelacion)
    {
        var existencias = await _existencias.ListarPorUbicacionAsync(ubicacionId, cancelacion);
        return existencias.Select(ExistenciaDto.Desde).ToList();
    }

    /// <summary>Historial de entradas y salidas de un SKU, del asiento mas reciente al mas antiguo.</summary>
    public async Task<IReadOnlyList<MovimientoExistenciaDto>> ListarMovimientosAsync(
        string sku,
        string? ubicacionId,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancelacion)
    {
        var movimientos = await _movimientos.ListarAsync(sku, ubicacionId, desde, hasta, cancelacion);
        return movimientos.Select(MovimientoExistenciaDto.Desde).ToList();
    }

    private const int PaginaPorDefecto = 1;
    private const int TamanoPorDefecto = 50;
    private const int TamanoMaximo = 200;
}
