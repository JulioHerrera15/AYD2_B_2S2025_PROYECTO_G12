using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Metricas;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Procesa <see cref="CertificacionSatFallidaData" /> (evento 2 de
/// facturacion-eventos-v1) y levanta la bandera de alerta fiscal sobre la orden
/// cuando esta se encuentra en CONFIRMADA_PENDIENTE_FISCAL. Si la orden ya paso
/// a CONFIRMADA (llegada fuera de orden), el mensaje se ignora y confirma.
///
/// La orden NO cambia de estado: se queda en CONFIRMADA_PENDIENTE_FISCAL con la
/// bandera levantada, esperando un reintento manual via
/// POST /api/ordenes/{id}/reintentar-certificacion (ADR-011).
/// </summary>
public sealed class ManejadorCertificacionSatFallida
{
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorCertificacionSatFallida> _logger;

    public ManejadorCertificacionSatFallida(
        IRepositorioOrdenes ordenes,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorCertificacionSatFallida> logger)
    {
        _ordenes = ordenes;
        _unidadDeTrabajo = unidadDeTrabajo;
        _logger = logger;
    }

    public async Task ManejarAsync(CertificacionSatFallidaData datos, CancellationToken cancelacion)
    {
        var orden = await _ordenes.ObtenerPorIdAsync(datos.OrdenId, cancelacion);

        if (orden is null)
        {
            _logger.LogWarning(
                "CertificacionSatFallida para orden {OrdenId} no encontrada; se confirma el mensaje.",
                datos.OrdenId);
            return;
        }

        if (orden.Estado is EstadoOrden.CONFIRMADA
            or EstadoOrden.DESPACHO_SOLICITADO
            or EstadoOrden.EN_PREPARACION
            or EstadoOrden.EN_TRANSITO
            or EstadoOrden.LISTA_PARA_RECOGER
            or EstadoOrden.ENTREGADA
            or EstadoOrden.DEVOLUCION_EN_PROCESO
            or EstadoOrden.DEVUELTA
            or EstadoOrden.CANCELADA
            or EstadoOrden.CANCELADA_RESERVA_VENCIDA
            or EstadoOrden.RECHAZADA)
        {
            _logger.LogInformation(
                "CertificacionSatFallida para orden {OrdenId} en estado {Estado}; llega fuera de orden. Se confirma el mensaje.",
                datos.OrdenId,
                orden.Estado);
            return;
        }

        if (orden.Estado != EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL)
        {
            _logger.LogWarning(
                "CertificacionSatFallida para orden {OrdenId} en estado {Estado} inesperado; se confirma sin cambios.",
                datos.OrdenId,
                orden.Estado);
            return;
        }

        var motivo = $"Certificacion SAT fallida: {datos.Intentos} intentos agotados. Ultimo error [{datos.UltimoCodigo}]: {datos.UltimoMotivo}";
        orden.LevantarAlertaFiscal(motivo, datos.FallidaEn);

        MetricasSgo.AlertasFiscalesLevantadas.Inc();

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        _logger.LogWarning(
            "Alerta fiscal levantada en orden {OrdenId}: {Motivo}",
            datos.OrdenId,
            motivo);
    }
}
