using Microsoft.Extensions.Logging;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Procesa <see cref="DocumentoFiscalCertificadoData" /> (evento 1 de
/// facturacion-eventos-v1) y cierra el tramo CONFIRMADA_PENDIENTE_FISCAL →
/// CONFIRMADA de la saga de Órdenes (ADR-011). Persiste los datos fiscales
/// en la orden y delega la transición a
/// <see cref="ServicioConfirmacionOrden.AplicarTransicionAsync" />, que encola
/// el <see cref="Contracts.Eventos.Ordenes.OrdenEstadoCambiado" /> (evento 2) y
/// el <see cref="Contracts.Eventos.Ordenes.OrdenCertificadaFiscalmente" />
/// (evento 12) en el outbox, todo dentro de una sola transacción de base de datos.
///
/// Comportamiento ante estados inesperados:
/// <list type="bullet">
///   <item>CONFIRMADA o posterior: idempotente, no hace nada y confirma el
///   mensaje.</item>
///   <item>CONFIRMADA_PENDIENTE_FISCAL: aplica la transición.</item>
///   <item>Cualquier otro estado: warning y confirmación sin cambiar nada.</item>
/// </list>
/// </summary>
public sealed class ManejadorDocumentoFiscalCertificado
{
    private readonly ServicioConfirmacionOrden _servicio;
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorDocumentoFiscalCertificado> _logger;

    public ManejadorDocumentoFiscalCertificado(
        ServicioConfirmacionOrden servicio,
        IRepositorioOrdenes ordenes,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorDocumentoFiscalCertificado> logger)
    {
        _servicio = servicio;
        _ordenes = ordenes;
        _unidadDeTrabajo = unidadDeTrabajo;
        _logger = logger;
    }

    public async Task ManejarAsync(DocumentoFiscalCertificadoData datos, CancellationToken cancelacion)
    {
        var orden = await _ordenes.ObtenerPorIdAsync(datos.OrdenId, cancelacion);

        if (orden is null)
        {
            _logger.LogWarning(
                "DocumentoFiscalCertificado para orden {OrdenId} no encontrada; se confirma el mensaje.",
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
                "DocumentoFiscalCertificado para orden {OrdenId} en estado {Estado}; ya esta certificada o en estado terminal. Se confirma el mensaje.",
                datos.OrdenId,
                orden.Estado);
            return;
        }

        if (orden.Estado != EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL)
        {
            _logger.LogWarning(
                "DocumentoFiscalCertificado para orden {OrdenId} en estado {Estado} inesperado; se confirma el mensaje sin cambios.",
                datos.OrdenId,
                orden.Estado);
            return;
        }

        Action<Orden> antesDeTransicionar = orden =>
            orden.AsignarCertificacionFiscal(
                datos.DocumentoFiscalId,
                datos.Serie,
                datos.NumeroDocumento,
                datos.NumeroAutorizacion,
                datos.Folio,
                datos.CertificadaEn);

        await _servicio.AplicarTransicionAsync(
            datos.OrdenId,
            EstadoOrden.CONFIRMADA,
            PasoSaga.FISCAL,
            motivo: null,
            esCompensacion: false,
            cancelacion,
            antesDeTransicionar: antesDeTransicionar);
    }
}
