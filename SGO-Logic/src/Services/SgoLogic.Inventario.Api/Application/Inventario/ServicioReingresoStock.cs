using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Reingresa al stock las unidades de una devolucion (HU-014, criterio "reingresa
/// las unidades devueltas a la ubicacion correspondiente").
///
/// A diferencia de liberar una reserva, aqui SI sube la cantidad fisica: la reserva
/// es un compromiso logico y la devolucion es mercaderia que vuelve al anaquel. Por
/// eso cada linea deja asiento en el ledger de movimientos (HU-016) con motivo
/// REINGRESO_DEVOLUCION, y el disponible sube por partida doble.
///
/// No llama a <c>SaveChangesAsync</c>: lo hace el consumidor.
/// </summary>
public sealed class ServicioReingresoStock
{
    private readonly IRepositorioExistencias _existencias;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<ServicioReingresoStock> _logger;

    public ServicioReingresoStock(
        IRepositorioExistencias existencias,
        IRepositorioMovimientos movimientos,
        IOutboxWriter outbox,
        ILogger<ServicioReingresoStock> logger)
    {
        _existencias = existencias;
        _movimientos = movimientos;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<bool> ReingresarAsync(SolicitudReingreso solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var ahora = DateTime.UtcNow;
        var reingresadas = new List<LineaReingresadaData>();

        foreach (var linea in solicitud.Lineas)
        {
            if (linea.Cantidad <= 0)
            {
                continue;
            }

            // Cada linea puede volver a una ubicacion distinta de la del encabezado:
            // el contrato v1.3 trae UbicacionOrigenReservaId por linea justamente
            // porque una orden pudo despacharse desde varias ubicaciones.
            var ubicacion = string.IsNullOrWhiteSpace(linea.UbicacionId)
                ? solicitud.UbicacionReingresoId
                : linea.UbicacionId;

            if (string.IsNullOrWhiteSpace(ubicacion))
            {
                _logger.LogWarning(
                    "La devolucion {DevolucionId} no dice a que ubicacion vuelve el SKU {Sku}; se omite esa linea.",
                    solicitud.DevolucionId,
                    linea.Sku);

                continue;
            }

            var existencia = await _existencias.ObtenerAsync(linea.Sku, ubicacion, cancelacion);

            if (existencia is null)
            {
                // La mercaderia vuelve a una ubicacion donde ese SKU nunca tuvo
                // existencia (devolucion en otra tienda). Se crea en cero y se
                // ajusta, para no perder unidades fisicas reales.
                existencia = new Existencia(linea.Sku, ubicacion, solicitud.TipoUbicacion, 0);
                await _existencias.AgregarAsync(existencia, cancelacion);
            }

            var cantidadAnterior = existencia.CantidadFisica;
            existencia.AjustarCantidadFisica(cantidadAnterior + linea.Cantidad);

            await _movimientos.AgregarAsync(
                new MovimientoExistencia(
                    existencia.Sku,
                    existencia.UbicacionId,
                    linea.Cantidad,
                    existencia.CantidadFisica,
                    MotivoMovimiento.REINGRESO_DEVOLUCION,
                    ahora),
                cancelacion);

            reingresadas.Add(new LineaReingresadaData(linea.Sku, linea.Cantidad, existencia.CantidadFisica));
        }

        if (reingresadas.Count == 0)
        {
            _logger.LogWarning(
                "La devolucion {DevolucionId} no reingreso ninguna unidad.",
                solicitud.DevolucionId);

            return false;
        }

        _outbox.Encolar(
            StockReingresado.EventType,
            StockReingresado.AggregateType,
            solicitud.DevolucionId,
            1,
            TopologiaMensajeria.ExchangeEventos,
            StockReingresado.RoutingKey,
            new StockReingresadoData(
                solicitud.DevolucionId,
                solicitud.OrdenOriginalId,
                solicitud.UbicacionReingresoId,
                reingresadas,
                ahora));

        _logger.LogInformation(
            "Devolucion {DevolucionId}: {Lineas} linea(s) reingresadas al stock.",
            solicitud.DevolucionId,
            reingresadas.Count);

        return true;
    }
}

/// <summary>Lo que Inventario necesita de una devolucion para devolver las unidades al anaquel.</summary>
public sealed record SolicitudReingreso(
    string DevolucionId,
    string OrdenOriginalId,
    string UbicacionReingresoId,
    TipoUbicacion TipoUbicacion,
    IReadOnlyList<LineaReingreso> Lineas);

/// <summary>Una linea devuelta. <paramref name="UbicacionId" /> vacio significa "la del encabezado".</summary>
public sealed record LineaReingreso(string Sku, int Cantidad, string? UbicacionId);
