using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Caso de uso de registro de devoluciones sobre ordenes entregadas (CU-14, HU-014).
/// Valida la solicitud contra el estado de la orden y las devoluciones previas,
/// genera el identificador DEV-&lt;anio&gt;-&lt;correlativo&gt;, persiste la entidad
/// Devolucion, encola DevolucionRegistrada y transiciona la orden a
/// DEVOLUCION_EN_PROCESO. Todo en una sola transaccion via
/// <see cref="IUnidadDeTrabajo" />.
/// </summary>
public sealed class ServicioDevolucion
{
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IOutboxWriter _outbox;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly IGeneradorCorrelativoDevolucion _generadorCorrelativo;
    private readonly ServicioConfirmacionOrden _servicioConfirmacion;

    public ServicioDevolucion(
        IRepositorioOrdenes ordenes,
        IOutboxWriter outbox,
        IUnidadDeTrabajo unidadDeTrabajo,
        IGeneradorCorrelativoDevolucion generadorCorrelativo,
        ServicioConfirmacionOrden servicioConfirmacion)
    {
        _ordenes = ordenes;
        _outbox = outbox;
        _unidadDeTrabajo = unidadDeTrabajo;
        _generadorCorrelativo = generadorCorrelativo;
        _servicioConfirmacion = servicioConfirmacion;
    }

    /// <summary>
    /// Registra una devolucion sobre una orden en estado ENTREGADA. Valida cantidades
    /// contra devoluciones previas, genera el ID, persiste y encola eventos.
    /// </summary>
    public async Task<DevolucionDto> RegistrarDevolucionAsync(
        SolicitudDevolucion solicitud,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (solicitud.Lineas is null || solicitud.Lineas.Count == 0)
        {
            throw new ArgumentException("La devolucion requiere al menos una linea.", nameof(solicitud));
        }

        var orden = await _ordenes.ObtenerPorIdAsync(solicitud.OrdenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(solicitud.OrdenId);

        if (orden.Estado != EstadoOrden.ENTREGADA)
        {
            throw new InvalidOperationException(
                $"La orden {solicitud.OrdenId} esta en estado {orden.Estado}. " +
                $"Solo se pueden registrar devoluciones sobre ordenes en estado ENTREGADA.");
        }

        var devolucionesExistentes = await _ordenes.ObtenerDevolucionesPorOrdenAsync(solicitud.OrdenId, cancelacion);
        var devolucionesValidas = devolucionesExistentes
            .Where(d => d.Estado is EstadoDevolucion.REGISTRADA or EstadoDevolucion.REEMBOLSO_APLICADO)
            .ToList();

        decimal montoReembolso = 0;
        var lineasDevolucion = new List<DevolucionLinea>();

        foreach (var lineaSolicitud in solicitud.Lineas)
        {
            var lineaOrden = orden.Lineas.FirstOrDefault(l => l.Sku == lineaSolicitud.Sku)
                ?? throw new ArgumentException(
                    $"El SKU {lineaSolicitud.Sku} no existe en la orden {solicitud.OrdenId}.");

            if (lineaSolicitud.Cantidad <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(solicitud.Lineas),
                    $"La cantidad devuelta para el SKU {lineaSolicitud.Sku} debe ser mayor que cero.");
            }

            int cantidadDevuelta = devolucionesValidas
                .SelectMany(d => d.Lineas)
                .Where(l => l.Sku == lineaSolicitud.Sku)
                .Sum(l => l.Cantidad);

            int cantidadDisponible = lineaOrden.Cantidad - cantidadDevuelta;
            if (lineaSolicitud.Cantidad > cantidadDisponible)
            {
                throw new ArgumentException(
                    $"La cantidad solicitada ({lineaSolicitud.Cantidad}) para el SKU {lineaSolicitud.Sku} " +
                    $"excede la cantidad disponible para devolucion ({cantidadDisponible}).");
            }

            var lineaDevolucion = new DevolucionLinea(
                lineaSolicitud.Sku,
                lineaSolicitud.Cantidad,
                lineaSolicitud.Motivo,
                lineaSolicitud.SkuReemplazo,
                lineaOrden.PrecioUnitario);

            lineasDevolucion.Add(lineaDevolucion);
            montoReembolso += lineaDevolucion.Subtotal;
        }

        bool esParcial = lineasDevolucion.Count != orden.Lineas.Count ||
            lineasDevolucion.Any(ld =>
            {
                var lo = orden.Lineas.First(l => l.Sku == ld.Sku);
                return ld.Cantidad < lo.Cantidad;
            });

        var correlativo = await _generadorCorrelativo.SiguienteAsync(cancelacion);
        var id = $"DEV-{DateTime.UtcNow:yyyy}-{correlativo.ToString("D6")}";

        var devolucion = new Devolucion(
            id,
            solicitud.OrdenId,
            orden.ClienteId,
            solicitud.Tipo,
            esParcial,
            solicitud.UbicacionReingresoId,
            montoReembolso,
            orden.PagoId ?? string.Empty,
            orden.DocumentoFiscalId ?? string.Empty,
            orden.Moneda.ToString(),
            solicitud.RegistradaPor,
            lineasDevolucion);

        await _ordenes.AgregarDevolucionAsync(devolucion, cancelacion);

        _outbox.Encolar(
            DevolucionRegistrada.EventType,
            DevolucionRegistrada.AggregateType,
            devolucion.Id,
            orden.Secuencia + 1,
            TopologiaMensajeria.ExchangeEventos,
            DevolucionRegistrada.RoutingKey,
            new DevolucionRegistradaData(
                DevolucionId: devolucion.Id,
                OrdenOriginalId: devolucion.OrdenOriginalId,
                ClienteId: devolucion.ClienteId,
                Tipo: devolucion.Tipo,
                EsParcial: devolucion.EsParcial,
                UbicacionReingresoId: devolucion.UbicacionReingresoId,
                Lineas: devolucion.Lineas
                    .Select(l => new DevolucionLineaData(l.Sku, l.Cantidad, l.Motivo, l.SkuReemplazo))
                    .ToList(),
                Moneda: devolucion.Moneda,
                MontoReembolso: devolucion.MontoReembolso.ToString(),
                PagoOriginalId: devolucion.PagoOriginalId,
                DocumentoFiscalOriginalId: devolucion.DocumentoFiscalOriginalId,
                RegistradaPor: devolucion.RegistradaPor,
                RegistradaEn: devolucion.RegistradaEn));

        await _servicioConfirmacion.AplicarTransicionAsync(
            solicitud.OrdenId,
            EstadoOrden.DEVOLUCION_EN_PROCESO,
            null,
            $"Devolucion {id} registrada",
            esCompensacion: false,
            cancelacion);

        return DevolucionDto.Desde(devolucion);
    }

    /// <summary>
    /// Obtiene todas las devoluciones de una orden.
    /// </summary>
    public async Task<IReadOnlyList<DevolucionDto>> ObtenerDevolucionesAsync(
        string ordenId,
        CancellationToken cancelacion)
    {
        var devoluciones = await _ordenes.ObtenerDevolucionesPorOrdenAsync(ordenId, cancelacion);
        return devoluciones.Select(DevolucionDto.Desde).ToList();
    }
}
