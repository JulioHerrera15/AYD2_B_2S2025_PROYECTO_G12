using Microsoft.Extensions.Logging;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Avanza la ruta de credito de la saga con la respuesta de Credito, con
/// deduplicacion por messageId (<see cref="IRegistroMensajesProcesados" />):
/// <list type="bullet">
///   <item><c>CreditoAprobado</c>: EVALUACION_CREDITO_SOLICITADA -&gt; CREDITO_APROBADO
///   y, en la misma transaccion, CONFIRMADA_PENDIENTE_FISCAL, igual que PagoAplicado.
///   La referencia del cargo queda en <see cref="Orden.PagoId" /> ("CRE-" + cargoId)
///   para que OrdenConfirmada y la certificacion fiscal sigan sin cambios.</item>
///   <item><c>CreditoRechazado</c>: RECHAZADA como compensacion, con el mensaje para
///   el cliente como detalle visible, y LiberacionReservaSolicitada con
///   CREDITO_RECHAZADO.</item>
/// </list>
/// Si la orden ya no esta en EVALUACION_CREDITO_SOLICITADA (timeout, reserva vencida,
/// cancelacion o evento tardio) se hace ack con warning y sin transicion: el cargo, si
/// lo hubo, lo anula Credito al consumir OrdenCancelada.
/// </summary>
public sealed class ManejadorEventosCredito
{
    /// <summary>Prefijo de la referencia de cargo guardada en Orden.PagoId.</summary>
    public const string PrefijoReferenciaCargo = "CRE-";

    /// <summary>Largo de la columna pago_id.</summary>
    public const int LargoMaximoPagoId = 128;

    private readonly IRegistroMensajesProcesados _registro;
    private readonly ServicioConfirmacionOrden _servicio;
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorEventosCredito> _logger;

    public ManejadorEventosCredito(
        IRegistroMensajesProcesados registro,
        ServicioConfirmacionOrden servicio,
        IRepositorioOrdenes ordenes,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorEventosCredito> logger)
    {
        _registro = registro;
        _servicio = servicio;
        _ordenes = ordenes;
        _unidadDeTrabajo = unidadDeTrabajo;
        _logger = logger;
    }

    public async Task<ResultadoEventoCredito> ManejarAsync(EventoCreditoRecibido evento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (await _registro.YaProcesadoAsync(evento.MessageId, cancelacion))
        {
            _logger.LogInformation(
                "Mensaje de Credito duplicado {MessageId} ({TipoEvento}); se hace ack sin aplicar.",
                evento.MessageId, evento.TipoEvento);
            return ResultadoEventoCredito.Duplicado;
        }

        if (evento.TipoEvento is not (CreditoAprobado.EventType or CreditoRechazado.EventType))
        {
            _logger.LogWarning(
                "Evento de Credito desconocido {TipoEvento} ({MessageId}); se hace ack sin aplicar.",
                evento.TipoEvento, evento.MessageId);
            return ResultadoEventoCredito.Ignorado;
        }

        var orden = await _ordenes.ObtenerPorIdAsync(evento.OrdenId, cancelacion);
        if (orden is null)
        {
            _logger.LogWarning(
                "{TipoEvento} para la orden {OrdenId} no encontrada ({MessageId}); se hace ack.",
                evento.TipoEvento, evento.OrdenId, evento.MessageId);
            return ResultadoEventoCredito.Ignorado;
        }

        if (orden.Estado != EstadoOrden.EVALUACION_CREDITO_SOLICITADA)
        {
            // Respuesta tardia: la orden ya termino (timeout, reserva vencida) o ya
            // avanzo. No se mueve; se marca el mensaje para no reinterpretarlo.
            _logger.LogWarning(
                "{TipoEvento} llego con la orden {OrdenId} en {Estado} ({MessageId}); ack sin transicion.",
                evento.TipoEvento, evento.OrdenId, orden.Estado, evento.MessageId);
            _registro.Registrar(evento.MessageId, evento.TipoEvento);
            await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
            return ResultadoEventoCredito.Ignorado;
        }

        _registro.Registrar(evento.MessageId, evento.TipoEvento);

        try
        {
            if (evento.TipoEvento == CreditoAprobado.EventType)
            {
                var referencia = ReferenciaCargo(evento.CargoId);
                await _servicio.AplicarTransicionAsync(
                    evento.OrdenId,
                    EstadoOrden.CREDITO_APROBADO,
                    PasoSaga.COBRO,
                    "Crédito aprobado",
                    esCompensacion: false,
                    cancelacion,
                    motivoLiberacion: null,
                    antesDeTransicionar: referencia is null ? null : o => o.RegistrarPago(referencia),
                    encadenadas: new[]
                    {
                        new TransicionEncadenada(
                            EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
                            PasoSaga.FISCAL,
                            "Credito aprobado: se solicita la certificacion fiscal")
                    });
            }
            else
            {
                await _servicio.AplicarTransicionAsync(
                    evento.OrdenId,
                    EstadoOrden.RECHAZADA,
                    PasoSaga.COBRO,
                    string.IsNullOrWhiteSpace(evento.MensajeCliente) ? MensajeRechazoPorDefecto : evento.MensajeCliente,
                    esCompensacion: true,
                    cancelacion,
                    MotivoLiberacionReserva.CREDITO_RECHAZADO);
            }
        }
        catch (ExcepcionTransicionInvalida ex)
        {
            _logger.LogWarning(
                ex,
                "Transicion invalida al aplicar {TipoEvento} sobre la orden {OrdenId} ({MessageId}); ack sin reencolar.",
                evento.TipoEvento, evento.OrdenId, evento.MessageId);
            return ResultadoEventoCredito.Ignorado;
        }

        return ResultadoEventoCredito.Aplicado;
    }

    /// <summary>Texto si Credito no mandara mensaje: nunca deberia pasar, pero el cliente siempre ve algo legible.</summary>
    public const string MensajeRechazoPorDefecto =
        "No pudimos aprobar tu compra a crédito. No se realizó ningún cargo; puedes pagar con tarjeta.";

    private static string? ReferenciaCargo(string? cargoId)
    {
        if (string.IsNullOrWhiteSpace(cargoId))
        {
            return null;
        }

        var referencia = PrefijoReferenciaCargo + cargoId.Trim();
        return referencia.Length > LargoMaximoPagoId ? referencia[..LargoMaximoPagoId] : referencia;
    }
}

/// <summary>
/// Datos ya interpretados de CreditoAprobado o CreditoRechazado que llegan por la
/// cola <c>ordenes.credito</c>.
/// </summary>
public sealed record EventoCreditoRecibido(
    string MessageId,
    string TransactionId,
    string TipoEvento,
    string OrdenId,
    string? CargoId = null,
    string? CodigoMotivo = null,
    string? MensajeCliente = null);

public enum ResultadoEventoCredito
{
    Aplicado,
    Duplicado,
    Ignorado
}
