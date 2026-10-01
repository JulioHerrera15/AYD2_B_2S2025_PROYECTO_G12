using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Barre las retenciones de carrito ACTIVA cuyo TTL vencio, devuelve sus unidades
/// al disponible y las marca EXPIRADA. Es el hermano de
/// <see cref="ServicioExpiracionReservas" /> para el agregado ReservaCarrito, con el
/// mismo criterio: una transaccion por retencion, y si una falla se descartan sus
/// cambios y se reintenta en el proximo ciclo porque sigue ACTIVA y vencida.
///
/// No publica eventos: la retencion de carrito nunca sale de Inventario. El cliente
/// se entera al consultar su carrito.
/// </summary>
public sealed class ServicioExpiracionReservasCarrito
{
    private readonly IRepositorioReservasCarrito _retenciones;
    private readonly IRepositorioExistencias _existencias;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly InventarioOptions _opciones;
    private readonly ILogger<ServicioExpiracionReservasCarrito> _logger;

    public ServicioExpiracionReservasCarrito(
        IRepositorioReservasCarrito retenciones,
        IRepositorioExistencias existencias,
        IUnidadDeTrabajo unidadDeTrabajo,
        IOptions<InventarioOptions> opciones,
        ILogger<ServicioExpiracionReservasCarrito> logger)
    {
        _retenciones = retenciones;
        _existencias = existencias;
        _unidadDeTrabajo = unidadDeTrabajo;
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <summary>Procesa un lote de retenciones vencidas y devuelve cuantas expiro de verdad.</summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken cancelacion)
    {
        var ahora = DateTime.UtcNow;
        var limite = _opciones.ExpiracionBatchSize > 0 ? _opciones.ExpiracionBatchSize : 20;

        var vencidas = await _retenciones.ListarActivasVencidasAsync(ahora, limite, cancelacion);
        if (vencidas.Count == 0)
        {
            return 0;
        }

        var expiradas = 0;

        // Igual que en el barrido de reservas: solo los ids, y cada retencion se relee
        // dentro de su iteracion para que un descarte no suelte las demas del lote.
        foreach (var id in vencidas.Select(retencion => retencion.Id).ToList())
        {
            cancelacion.ThrowIfCancellationRequested();

            if (await ExpirarUnaAsync(id, ahora, cancelacion))
            {
                expiradas++;
            }
        }

        return expiradas;
    }

    private async Task<bool> ExpirarUnaAsync(Guid id, DateTime ahora, CancellationToken cancelacion)
    {
        try
        {
            var retencion = await _retenciones.ObtenerPorIdAsync(id, cancelacion);

            if (retencion is null || !retencion.EstaActiva || !retencion.HaVencido(ahora))
            {
                return false;
            }

            var existencia = await _existencias.ObtenerAsync(retencion.Sku, retencion.UbicacionId, cancelacion);
            if (existencia is null)
            {
                _logger.LogWarning(
                    "La retencion {RetencionId} del carrito {CarritoId} referencia {Sku} en {UbicacionId}, sin existencia registrada.",
                    retencion.Id,
                    retencion.CarritoId,
                    retencion.Sku,
                    retencion.UbicacionId);
            }
            else
            {
                existencia.LiberarReserva(retencion.Cantidad);
            }

            retencion.MarcarExpirada();

            // Una transaccion por retencion: existencia liberada + retencion EXPIRADA.
            await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

            _logger.LogInformation(
                "Retencion de {Cantidad} unidad(es) de {Sku} del carrito {CarritoId} expiro; volvieron al disponible.",
                retencion.Cantidad,
                retencion.Sku,
                retencion.CarritoId);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _unidadDeTrabajo.DescartarCambios();

            _logger.LogWarning(
                ex,
                "No se pudo expirar la retencion de carrito {RetencionId}; se reintentara en el proximo barrido.",
                id);

            return false;
        }
    }
}
