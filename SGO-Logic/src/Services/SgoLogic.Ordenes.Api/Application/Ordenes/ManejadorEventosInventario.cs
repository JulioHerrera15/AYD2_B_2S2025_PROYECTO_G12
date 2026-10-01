using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Avanza la saga de la orden a partir de los eventos de resultado de Inventario
/// (contrato inventario-eventos-v1). Maneja tres tipos de evento:
///
/// <list type="bullet">
///   <item><see cref="InventarioReservadoData" />: la orden pasa de
///   RESERVA_SOLICITADA a INVENTARIO_RESERVADO y, en la misma transaccion, avanza
///   a COBRO_SOLICITADO emitiendo <c>CobroSolicitado</c>. Persiste el
///   <see cref="Orden.ReservaId" /> real.</item>
///   <item><see cref="ReservaInventarioRechazadaData" />: la orden pasa de
///   RESERVA_SOLICITADA a RECHAZADA (motivo STOCK_INSUFICIENTE). No se cobra.</item>
///   <item><see cref="ReservaInventarioExpiradaData" />: la orden pasa de
///   RESERVA_SOLICITADA a CANCELADA_RESERVA_VENCIDA.</item>
/// </list>
///
/// La transicion RESERVA_SOLICITADA → INVENTARIO_RESERVADO → COBRO_SOLICITADO se
/// ejecuta en una unica transaccion de base de datos: las dos transiciones de
/// dominio, sus filas de outbox y la marca de idempotencia se persisten con un
/// unico <c>SaveChangesAsync</c>.
///
/// Politica de errores (misma que ManejadorEventosPagos):
/// <list type="bullet">
///   <item>Transicion invalida o evento fuera de orden: warning y ACK, sin requeue.</item>
///   <item>Fallo transitorio (base caida, broker, orden aun no visible): NACK con
///   requeue, para que <c>x-delivery-limit</c> y el dead-letter actuen.</item>
///   <item>Un mensaje malo nunca tumba el proceso.</item>
/// </list>
/// </summary>
public sealed class ManejadorEventosInventario
{
    /// <summary>Motivo visible para el cliente cuando Inventario rechaza la reserva.</summary>
    public const string MotivoReservaRechazada = "El producto ya fue reservado previamente";

    private readonly IRegistroMensajesProcesados _registro;
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IOutboxWriter _outbox;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorEventosInventario> _logger;

    public ManejadorEventosInventario(
        IRegistroMensajesProcesados registro,
        IRepositorioOrdenes ordenes,
        IOutboxWriter outbox,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorEventosInventario> logger)
    {
        _registro = registro;
        _ordenes = ordenes;
        _outbox = outbox;
        _unidadDeTrabajo = unidadDeTrabajo;
        _logger = logger;
    }

    public async Task<ResultadoEventoInventario> ManejarAsync(EventoInventarioRecibido evento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (await _registro.YaProcesadoAsync(evento.MessageId, cancelacion))
        {
            _logger.LogInformation(
                "Mensaje de Inventario duplicado {MessageId} ({TipoEvento}); se hace ack sin aplicar.",
                evento.MessageId,
                evento.TipoEvento);
            return ResultadoEventoInventario.Duplicado;
        }

        if (!TryMapearDestino(evento.TipoEvento, out var estadoDestino, out var esCompensacion))
        {
            _logger.LogWarning(
                "Evento de Inventario desconocido {TipoEvento} ({MessageId}) en la orden {OrdenId}; se hace ack sin aplicar.",
                evento.TipoEvento,
                evento.MessageId,
                evento.OrdenId);
            return ResultadoEventoInventario.Ignorado;
        }

        var orden = await _ordenes.ObtenerPorIdAsync(evento.OrdenId, cancelacion);

        if (orden is null)
        {
            _logger.LogWarning(
                "Evento de Inventario {TipoEvento} para orden {OrdenId} no encontrada; se confirma el mensaje.",
                evento.TipoEvento,
                evento.OrdenId);
            return ResultadoEventoInventario.Ignorado;
        }

        if (evento.TipoEvento == InventarioReservado.EventType && orden.Estado == EstadoOrden.CANCELADA)
        {
            // El cliente cancelo mientras la reserva estaba en camino (la orden no
            // tenia ReservaId y por eso no se pidio liberar nada). La orden no se
            // mueve; se pide liberar la reserva que Inventario acaba de crear.
            _registro.Registrar(evento.MessageId, evento.TipoEvento);
            await LiberarReservaTardiaAsync(orden, (InventarioReservadoData)evento.Datos, cancelacion);
            return ResultadoEventoInventario.Aplicado;
        }

        if (orden.Estado == estadoDestino)
        {
            _registro.Registrar(evento.MessageId, evento.TipoEvento);
            await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);
            return ResultadoEventoInventario.Aplicado;
        }

        if (!MaquinaEstadosOrden.EsTransicionValida(orden.Estado, estadoDestino))
        {
            _logger.LogWarning(
                "Transicion {Desde} -> {Hacia} invalida al aplicar {TipoEvento} sobre la orden {OrdenId} ({MessageId}); se hace ack sin reencolar.",
                orden.Estado,
                estadoDestino,
                evento.TipoEvento,
                evento.OrdenId,
                evento.MessageId);
            return ResultadoEventoInventario.Ignorado;
        }

        _registro.Registrar(evento.MessageId, evento.TipoEvento);

        try
        {
            switch (evento.TipoEvento)
            {
                case InventarioReservado.EventType:
                    await ManejarReservaConfirmadaAsync(orden, (InventarioReservadoData)evento.Datos, cancelacion);
                    break;

                case ReservaInventarioRechazada.EventType:
                    await ManejarReservaRechazadaAsync(orden, estadoDestino, cancelacion);
                    break;

                case ReservaInventarioExpirada.EventType:
                    await ManejarReservaExpiradaAsync(orden, cancelacion);
                    break;
            }
        }
        catch (ExcepcionTransicionInvalida ex)
        {
            _logger.LogWarning(
                ex,
                "Transicion invalida al aplicar {TipoEvento} sobre la orden {OrdenId} ({MessageId}); se hace ack sin reencolar.",
                evento.TipoEvento,
                evento.OrdenId,
                evento.MessageId);
            return ResultadoEventoInventario.Ignorado;
        }

        return ResultadoEventoInventario.Aplicado;
    }

    private async Task ManejarReservaConfirmadaAsync(
        Orden orden,
        InventarioReservadoData datos,
        CancellationToken cancelacion)
    {
        orden.RegistrarReserva(datos.ReservaId, datos.UbicacionId);

        var transicion1 = orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO, pasoSaga: PasoSaga.RESERVA);

        _outbox.Encolar(
            OrdenEstadoCambiado.EventType,
            OrdenEstadoCambiado.AggregateType,
            orden.Id,
            transicion1.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            OrdenEstadoCambiado.RoutingKey,
            new OrdenEstadoCambiadoData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                EstadoAnterior: transicion1.EstadoAnterior,
                EstadoNuevo: transicion1.EstadoNuevo,
                Motivo: transicion1.Motivo,
                PasoSaga: transicion1.PasoSaga,
                EsCompensacion: transicion1.EsCompensacion,
                CambiadoEn: transicion1.CambiadoEn));

        MapaEventosSagaOrden.Encolar(orden, transicion1, _outbox);

        var transicion2 = orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO, pasoSaga: PasoSaga.COBRO);

        _outbox.Encolar(
            OrdenEstadoCambiado.EventType,
            OrdenEstadoCambiado.AggregateType,
            orden.Id,
            transicion2.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            OrdenEstadoCambiado.RoutingKey,
            new OrdenEstadoCambiadoData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                EstadoAnterior: transicion2.EstadoAnterior,
                EstadoNuevo: transicion2.EstadoNuevo,
                Motivo: transicion2.Motivo,
                PasoSaga: transicion2.PasoSaga,
                EsCompensacion: transicion2.EsCompensacion,
                CambiadoEn: transicion2.CambiadoEn));

        MapaEventosSagaOrden.Encolar(orden, transicion2, _outbox);

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Reserva {ReservaId} confirmada para la orden {OrdenId}; avance a INVENTARIO_RESERVADO y COBRO_SOLICITADO.",
            datos.ReservaId,
            orden.Id);
    }

    private async Task LiberarReservaTardiaAsync(
        Orden orden,
        InventarioReservadoData datos,
        CancellationToken cancelacion)
    {
        orden.RegistrarReserva(datos.ReservaId, datos.UbicacionId);

        _outbox.Encolar(
            LiberacionReservaSolicitada.EventType,
            LiberacionReservaSolicitada.AggregateType,
            orden.Id,
            orden.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            LiberacionReservaSolicitada.RoutingKey,
            new LiberacionReservaSolicitadaData(
                OrdenId: orden.Id,
                ReservaId: datos.ReservaId,
                Motivo: MotivoLiberacionReserva.CANCELACION_CLIENTE,
                SolicitadaEn: DateTime.UtcNow));

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Reserva {ReservaId} llego para la orden {OrdenId} ya cancelada; se pidio su liberacion.",
            datos.ReservaId,
            orden.Id);
    }

    private async Task ManejarReservaRechazadaAsync(
        Orden orden,
        EstadoOrden estadoDestino,
        CancellationToken cancelacion)
    {
        // El motivo viaja en OrdenEstadoCambiado y OrdenCancelada hasta ConsultaEstado,
        // que es lo que ve el cliente: por eso va en lenguaje de cliente.
        var transicion = orden.TransicionarA(estadoDestino, motivo: MotivoReservaRechazada, pasoSaga: PasoSaga.RESERVA, esCompensacion: true);

        _outbox.Encolar(
            OrdenEstadoCambiado.EventType,
            OrdenEstadoCambiado.AggregateType,
            orden.Id,
            transicion.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            OrdenEstadoCambiado.RoutingKey,
            new OrdenEstadoCambiadoData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                EstadoAnterior: transicion.EstadoAnterior,
                EstadoNuevo: transicion.EstadoNuevo,
                Motivo: transicion.Motivo,
                PasoSaga: transicion.PasoSaga,
                EsCompensacion: transicion.EsCompensacion,
                CambiadoEn: transicion.CambiadoEn));

        MapaEventosSagaOrden.Encolar(orden, transicion, _outbox);

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Reserva rechazada para la orden {OrdenId}; orden llevada a {Estado}.",
            orden.Id,
            estadoDestino);
    }

    private async Task ManejarReservaExpiradaAsync(
        Orden orden,
        CancellationToken cancelacion)
    {
        var transicion = orden.TransicionarA(EstadoOrden.CANCELADA_RESERVA_VENCIDA, motivo: "Reserva expirada por TTL", pasoSaga: PasoSaga.RESERVA);

        _outbox.Encolar(
            OrdenEstadoCambiado.EventType,
            OrdenEstadoCambiado.AggregateType,
            orden.Id,
            transicion.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            OrdenEstadoCambiado.RoutingKey,
            new OrdenEstadoCambiadoData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                EstadoAnterior: transicion.EstadoAnterior,
                EstadoNuevo: transicion.EstadoNuevo,
                Motivo: transicion.Motivo,
                PasoSaga: transicion.PasoSaga,
                EsCompensacion: transicion.EsCompensacion,
                CambiadoEn: transicion.CambiadoEn));

        MapaEventosSagaOrden.Encolar(orden, transicion, _outbox);

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Reserva expirada para la orden {OrdenId}; orden cancelada por reserva vencida.",
            orden.Id);
    }

    private static bool TryMapearDestino(
        string tipoEvento,
        out EstadoOrden estadoDestino,
        out bool esCompensacion)
    {
        switch (tipoEvento)
        {
            case InventarioReservado.EventType:
                estadoDestino = EstadoOrden.INVENTARIO_RESERVADO;
                esCompensacion = false;
                return true;

            case ReservaInventarioRechazada.EventType:
                estadoDestino = EstadoOrden.RECHAZADA;
                esCompensacion = true;
                return true;

            case ReservaInventarioExpirada.EventType:
                estadoDestino = EstadoOrden.CANCELADA_RESERVA_VENCIDA;
                esCompensacion = false;
                return true;

            default:
                estadoDestino = default;
                esCompensacion = false;
                return false;
        }
    }
}

/// <summary>
/// Datos ya interpretados de un evento de resultado de Inventario que llega por
/// la cola <c>ordenes.inventario</c>. El consumidor arma este registro a partir
/// del sobre y su <c>data</c>; el manejador de la saga solo depende de estos
/// campos, no del transporte.
/// </summary>
public sealed record EventoInventarioRecibido(
    string MessageId,
    string TransactionId,
    string TipoEvento,
    string OrdenId,
    object Datos);

/// <summary>Desenlace de aplicar un evento de Inventario a la saga de la orden.</summary>
public enum ResultadoEventoInventario
{
    /// <summary>La transicion se aplico y se encolaron los eventos derivados.</summary>
    Aplicado,

    /// <summary>El messageId ya estaba registrado: no se aplico nada.</summary>
    Duplicado,

    /// <summary>
    /// Transicion invalida o evento desconocido: se registra un warning y se hace
    /// ack sin reencolar (un mensaje que nunca va a aplicar no debe entrar en bucle).
    /// </summary>
    Ignorado
}
