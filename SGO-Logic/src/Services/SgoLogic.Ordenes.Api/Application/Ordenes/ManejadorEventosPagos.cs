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
/// Tambien maneja los resultados de reembolso (ReembolsoAplicado, ReembolsoFallido)
/// que transicionan la orden de DEVOLUCION_EN_PROCESO a DEVUELTA (devolucion total)
/// o de vuelta a ENTREGADA (devolucion parcial, para permitir devoluciones
/// posteriores sobre las unidades restantes), o la dejan en DEVOLUCION_EN_PROCESO
/// ante ReembolsoFallido.
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
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorEventosPagos> _logger;

    public ManejadorEventosPagos(
        IRegistroMensajesProcesados registro,
        ServicioConfirmacionOrden servicio,
        IRepositorioOrdenes ordenes,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorEventosPagos> logger)
    {
        _registro = registro;
        _servicio = servicio;
        _ordenes = ordenes;
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

        if (EsEventoReembolso(evento.TipoEvento))
        {
            return await ManejarReembolsoAsync(evento, cancelacion);
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

        // Solo PagoAplicado trae un identificador real de pago (autorizacionId);
        // se persiste en la orden en el mismo momento en que se aplica la
        // transicion, para que reemplace el placeholder PAG-PENDIENTE-<id> que
        // usan OrdenConfirmada y OrdenCancelada (item 7). Si por algun motivo el
        // evento no lo trae, no se llama a RegistrarPago: Orden.PagoId se queda en
        // null y MapaEventosSagaOrden sigue cayendo al placeholder.
        Action<Orden>? antesDeTransicionar =
            estadoDestino == EstadoOrden.PAGO_APLICADO && !string.IsNullOrWhiteSpace(evento.AutorizacionId)
                ? orden => orden.RegistrarPago(evento.AutorizacionId!)
                : null;

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
                motivoLiberacion,
                antesDeTransicionar);
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

    private async Task<ResultadoEventoPago> ManejarReembolsoAsync(
        EventoPagoRecibido evento,
        CancellationToken cancelacion)
    {
        _registro.Registrar(evento.MessageId, evento.TipoEvento);

        var devolucion = await _ordenes.ObtenerDevolucionPorIdAsync(evento.DevolucionId!, cancelacion);
        if (devolucion is null)
        {
            _logger.LogWarning(
                "Devolucion {DevolucionId} no encontrada al procesar {TipoEvento} ({MessageId}); se hace ack.",
                evento.DevolucionId,
                evento.TipoEvento,
                evento.MessageId);
            return ResultadoEventoPago.Ignorado;
        }

        if (evento.TipoEvento == ReembolsoAplicado.EventType)
        {
            devolucion.MarcarReembolsoAplicado();

            // Una devolucion parcial deja unidades pendientes de devolver: la orden
            // vuelve a ENTREGADA (transicion valida desde DEVOLUCION_EN_PROCESO) en
            // vez de cerrarse en DEVUELTA, que es terminal y bloquearia devoluciones
            // futuras sobre las unidades restantes.
            var estadoDestino = devolucion.EsParcial ? EstadoOrden.ENTREGADA : EstadoOrden.DEVUELTA;

            ResultadoTransicionOrdenDto resultado;
            try
            {
                resultado = await _servicio.AplicarTransicionAsync(
                    devolucion.OrdenOriginalId,
                    estadoDestino,
                    null,
                    $"Reembolso {evento.DevolucionId} aplicado",
                    esCompensacion: false,
                    cancelacion);
            }
            catch (ExcepcionTransicionInvalida ex)
            {
                _logger.LogWarning(
                    ex,
                    "Transicion invalida al aplicar ReembolsoAplicado sobre la orden {OrdenId} ({MessageId}); se hace ack sin reencolar.",
                    devolucion.OrdenOriginalId,
                    evento.MessageId);
                return ResultadoEventoPago.Ignorado;
            }

            if (resultado.SinCambios)
            {
                await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
            }
        }
        else
        {
            // ReembolsoFallido: se marca la devolucion pero la orden se queda
            // en DEVOLUCION_EN_PROCESO para permitir un reintento posterior.
            devolucion.MarcarReembolsoFallido();
            await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
        }

        return ResultadoEventoPago.Aplicado;
    }

    private static bool EsEventoReembolso(string tipoEvento) =>
        tipoEvento is ReembolsoAplicado.EventType or ReembolsoFallido.EventType;

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
