using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Nucleo de HU-002 (EV-23). Barre las reservas cuyo TTL vencio, devuelve sus
/// unidades al disponible y las marca EXPIRADA, sin intervencion manual.
///
/// A diferencia de <see cref="ServicioReservas" />, este servicio SI confirma la
/// transaccion, y lo hace una vez por reserva: el criterio de aceptacion pide que
/// un fallo se reintente sin liberar prematuramente, y eso solo se cumple si el
/// destino de una reserva no depende del de las demas del lote. Si una falla se
/// descartan sus cambios, se sigue con la siguiente, y la que fallo se vuelve a
/// tomar en el proximo ciclo porque sigue ACTIVA y vencida.
///
/// La logica vive aca y no en el <c>BackgroundService</c> para poder probarla sin
/// depender de un temporizador real.
/// </summary>
public sealed class ServicioExpiracionReservas
{
    private readonly IRepositorioReservas _reservas;
    private readonly IRepositorioExistencias _existencias;
    private readonly IOutboxWriter _outbox;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly InventarioOptions _opciones;
    private readonly ILogger<ServicioExpiracionReservas> _logger;

    public ServicioExpiracionReservas(
        IRepositorioReservas reservas,
        IRepositorioExistencias existencias,
        IOutboxWriter outbox,
        IUnidadDeTrabajo unidadDeTrabajo,
        IOptions<InventarioOptions> opciones,
        ILogger<ServicioExpiracionReservas> logger)
    {
        _reservas = reservas;
        _existencias = existencias;
        _outbox = outbox;
        _unidadDeTrabajo = unidadDeTrabajo;
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <summary>
    /// Procesa un lote de reservas vencidas y devuelve cuantas se expiraron de
    /// verdad. Nunca propaga el fallo de una reserva individual.
    /// </summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken cancelacion)
    {
        var ahora = DateTime.UtcNow;
        var limite = _opciones.ExpiracionBatchSize > 0 ? _opciones.ExpiracionBatchSize : 20;

        var vencidas = await _reservas.ListarActivasVencidasAsync(ahora, limite, cancelacion);
        if (vencidas.Count == 0)
        {
            return 0;
        }

        var expiradas = 0;

        // Se guardan solo los identificadores y cada reserva se relee dentro de su
        // propia iteracion: descartar los cambios de una que fallo suelta todo lo
        // rastreado, incluidas las reservas del lote que todavia no se tocaron.
        foreach (var reservaId in vencidas.Select(reserva => reserva.Id).ToList())
        {
            cancelacion.ThrowIfCancellationRequested();

            if (await ExpirarUnaAsync(reservaId, ahora, cancelacion))
            {
                expiradas++;
            }
        }

        return expiradas;
    }

    private async Task<bool> ExpirarUnaAsync(string reservaId, DateTime ahora, CancellationToken cancelacion)
    {
        try
        {
            var reserva = await _reservas.ObtenerPorIdAsync(reservaId, cancelacion);

            // Red de seguridad: el filtro del repositorio ya descarta lo que no
            // corresponde, pero el dominio es quien manda sobre si una reserva
            // puede expirar.
            if (reserva is null || !reserva.EstaActiva || !reserva.HaVencido(ahora))
            {
                return false;
            }

            foreach (var linea in reserva.Lineas)
            {
                var existencia = await _existencias.ObtenerAsync(linea.Sku, reserva.UbicacionId, cancelacion);

                if (existencia is null)
                {
                    // No deberia pasar: la existencia se creo al reservar. Si pasa, la
                    // reserva se expira igual (no puede quedar reteniendo stock para
                    // siempre) y queda constancia para revisar el dato.
                    _logger.LogWarning(
                        "La reserva {ReservaId} referencia el SKU {Sku} en la ubicacion {UbicacionId}, que ya no tiene existencia registrada.",
                        reserva.Id,
                        linea.Sku,
                        reserva.UbicacionId);
                    continue;
                }

                existencia.LiberarReserva(linea.Cantidad);
            }

            reserva.MarcarExpirada();

            _outbox.Encolar(
                ReservaInventarioExpirada.EventType,
                ReservaInventarioExpirada.AggregateType,
                reserva.Id,
                reserva.Secuencia,
                TopologiaMensajeria.ExchangeEventos,
                ReservaInventarioExpirada.RoutingKey,
                new ReservaInventarioExpiradaData(
                    ReservaId: reserva.Id,
                    OrdenId: reserva.OrdenId,
                    UbicacionId: reserva.UbicacionId,
                    Lineas: reserva.Lineas
                        .Select(linea => new ReservaConfirmadaLineaData(linea.Sku, linea.Cantidad))
                        .ToList(),
                    ExpiraEn: reserva.ExpiraEn,
                    ExpiradaEn: ahora));

            // Una transaccion por reserva: existencias liberadas + reserva EXPIRADA +
            // fila de outbox.
            await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

            _logger.LogInformation(
                "Reserva {ReservaId} de la orden {OrdenId} expiro por TTL; sus unidades volvieron al disponible.",
                reserva.Id,
                reserva.OrdenId);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nada se libero: el descarte suelta los cambios de esta reserva para que
            // no contaminen el intento de la siguiente. La reserva sigue ACTIVA y
            // vencida, asi que el proximo ciclo la vuelve a tomar.
            _unidadDeTrabajo.DescartarCambios();

            _logger.LogWarning(
                ex,
                "No se pudo expirar la reserva {ReservaId}; se reintentara en el proximo barrido.",
                reservaId);

            return false;
        }
    }
}
