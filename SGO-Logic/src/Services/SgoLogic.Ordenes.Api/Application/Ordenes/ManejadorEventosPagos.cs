using Microsoft.Extensions.Logging;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Avanza la saga de la orden (CU-03) a partir de un evento de resultado de Pagos,
/// con deduplicacion por messageId y compensacion ante falla de cobro. Cierra la
/// casilla de compensacion de HU-003.
///
/// Mapeo de eventos a estado destino de <see cref="MaquinaEstadosOrden" /> desde
/// <see cref="EstadoOrden.COBRO_SOLICITADO" />:
/// <list type="bullet">
///   <item><c>PagoAplicado</c> -&gt; <see cref="EstadoOrden.PAGO_APLICADO" />.</item>
///   <item><c>PagoRechazado</c> y <c>PagoPendiente</c> -&gt; <see cref="EstadoOrden.PAGO_PENDIENTE" />,
///   marcada como compensacion: <see cref="ServicioConfirmacionOrden" /> encola
///   ademas <c>LiberacionReservaSolicitada</c> por outbox. El motivo se deriva del
///   evento de origen, no del estado destino: <c>PagoRechazado</c> -&gt;
///   <see cref="MotivoLiberacionReserva.COBRO_RECHAZADO" /> (el proveedor si
///   respondio), <c>PagoPendiente</c> -&gt;
///   <see cref="MotivoLiberacionReserva.COBRO_SIN_RESPUESTA" />.</item>
/// </list>
/// Toda transicion persiste su <c>TransicionOrden</c> y publica
/// <c>OrdenEstadoCambiado</c> por outbox, igual que el resto del contexto. La
/// marca de idempotencia se guarda en el mismo <c>SaveChangesAsync</c>.
/// </summary>
public sealed class ManejadorEventosPagos
{
    private const PasoSaga PasoDeCobro = PasoSaga.COBRO;

    private readonly IRegistroMensajesProcesados _registro;
    private readonly ServicioConfirmacionOrden _servicio;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorEventosPagos> _logger;

    public ManejadorEventosPagos(
        IRegistroMensajesProcesados registro,
        ServicioConfirmacionOrden servicio,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorEventosPagos> logger)
    {
        _registro = registro;
        _servicio = servicio;
        _unidadDeTrabajo = unidadDeTrabajo;
        _logger = logger;
    }

    public async Task<ResultadoEventoPago> ManejarAsync(EventoPagoRecibido evento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (await _registro.YaProcesadoAsync(evento.MessageId, cancelacion))
        {
            _logger.LogInformation(
                "Mensaje de Pagos duplicado {MessageId} ({TipoEvento}); se hace ack sin aplicar.",
                evento.MessageId,
                evento.TipoEvento);
            return ResultadoEventoPago.Duplicado;
        }

        if (!TryMapearDestino(evento.TipoEvento, out var estadoDestino, out var esCompensacion, out var motivoLiberacion))
        {
            _logger.LogWarning(
                "Evento de Pagos desconocido {TipoEvento} ({MessageId}) en la orden {OrdenId}; se hace ack sin aplicar.",
                evento.TipoEvento,
                evento.MessageId,
                evento.OrdenId);
            return ResultadoEventoPago.Ignorado;
        }

        // La marca de idempotencia se agrega a la unidad de trabajo antes de la
        // transicion: se persiste en el mismo SaveChangesAsync que la transicion y
        // las filas de outbox.
        _registro.Registrar(evento.MessageId, evento.TipoEvento);

        ResultadoTransicionOrdenDto resultado;
        try
        {
            resultado = await _servicio.AplicarTransicionAsync(
                evento.OrdenId,
                estadoDestino,
                PasoDeCobro,
                evento.Motivo,
                esCompensacion,
                cancelacion,
                motivoLiberacion);
        }
        catch (ExcepcionTransicionInvalida ex)
        {
            // La orden no esta en un estado desde el que este evento aplique
            // (reintento tardio, evento fuera de orden). Warning y ack: reencolar
            // solo repetiria el mismo fallo en bucle.
            _logger.LogWarning(
                ex,
                "Transicion invalida al aplicar {TipoEvento} sobre la orden {OrdenId} ({MessageId}); se hace ack sin reencolar.",
                evento.TipoEvento,
                evento.OrdenId,
                evento.MessageId);
            return ResultadoEventoPago.Ignorado;
        }

        if (resultado.SinCambios)
        {
            // La orden ya estaba en el estado destino: AplicarTransicionAsync no
            // guarda nada en ese caso. Se persiste igual la marca para no volver a
            // interpretar este messageId.
            await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        }

        return ResultadoEventoPago.Aplicado;
    }

    private static bool TryMapearDestino(
        string tipoEvento,
        out EstadoOrden estadoDestino,
        out bool esCompensacion,
        out MotivoLiberacionReserva? motivoLiberacion)
    {
        switch (tipoEvento)
        {
            case PagoAplicado.EventType:
                estadoDestino = EstadoOrden.PAGO_APLICADO;
                esCompensacion = false;
                motivoLiberacion = null;
                return true;

            case PagoRechazado.EventType:
                // El proveedor respondio con un rechazo explicito.
                estadoDestino = EstadoOrden.PAGO_PENDIENTE;
                esCompensacion = true;
                motivoLiberacion = MotivoLiberacionReserva.COBRO_RECHAZADO;
                return true;

            case PagoPendiente.EventType:
                // Timeout, 5xx o error de red: el cobro quedo sin respuesta.
                estadoDestino = EstadoOrden.PAGO_PENDIENTE;
                esCompensacion = true;
                motivoLiberacion = MotivoLiberacionReserva.COBRO_SIN_RESPUESTA;
                return true;

            default:
                estadoDestino = default;
                esCompensacion = false;
                motivoLiberacion = null;
                return false;
        }
    }
}
