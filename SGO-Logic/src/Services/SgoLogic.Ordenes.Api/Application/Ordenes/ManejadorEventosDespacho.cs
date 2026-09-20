using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Avanza la orden a lo largo del tramo de entrega a partir de los eventos de
/// Despacho (contrato despacho-eventos-v1): <c>DespachoEstadoActualizado</c> y
/// <c>PedidoListoParaRecoger</c>. Los estados de despacho llegan como texto en el
/// contrato; aqui se traducen a un <see cref="EstadoOrden" /> que YA existe en
/// <see cref="MaquinaEstadosOrden" />, sin inventar estados de orden.
///
/// <list type="table">
///   <listheader><term>EstadoDespacho</term><description>EstadoOrden</description></listheader>
///   <item><term>EN_PREPARACION</term><description>EN_PREPARACION</description></item>
///   <item><term>EN_TRANSITO</term><description>EN_TRANSITO</description></item>
///   <item><term>LISTA_PARA_RECOGER</term><description>LISTA_PARA_RECOGER</description></item>
///   <item><term>ENTREGADA</term><description>ENTREGADA (tambien la recogida en tienda)</description></item>
/// </list>
///
/// ORIGEN_PENDIENTE, RECOGIDA_TIENDA, GUIA_SOLICITADA, GUIA_ASIGNADA,
/// GUIA_PENDIENTE, INCIDENCIA y ANULADA no tienen equivalente en la orden: solo se
/// registran en el log y no mueven nada. ANULADA ademas es eco de una cancelacion
/// que la propia orden ya origino.
///
/// Eventos fuera de orden:
/// <list type="bullet">
///   <item>Si la orden ya paso ese estado (o esta en el mismo), el evento se
///   ignora sin excepcion.</item>
///   <item>Si llega un estado posterior saltandose uno intermedio (por ejemplo
///   ENTREGADA con la orden en DESPACHO_SOLICITADO), se aplican las transiciones
///   intermedias en la misma transaccion. La etapa intermedia depende del metodo
///   de entrega de la orden: EN_TRANSITO a domicilio, LISTA_PARA_RECOGER en
///   recogida en tienda.</item>
/// </list>
///
/// Cada transicion aplicada encola lo mismo que cualquier otra transicion de
/// Ordenes: <c>OrdenEstadoCambiado</c> mas el evento de saga que el mapa asocie al
/// estado destino. Todas las transiciones, sus filas de outbox y la marca de
/// idempotencia se persisten con un unico <c>SaveChangesAsync</c>.
///
/// Politica de errores (misma que ManejadorEventosInventario): evento fuera de
/// lugar o desconocido, warning y ACK sin requeue; fallo transitorio, NACK.
/// </summary>
public sealed class ManejadorEventosDespacho
{
    private const string EstadoListaParaRecoger = "LISTA_PARA_RECOGER";

    /// <summary>
    /// Estados de despacho que llevan la orden a un estado nuevo. El resto se
    /// registra en el log (ver documentacion de la clase).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, EstadoOrden> EstadoOrdenPorEstadoDespacho =
        new Dictionary<string, EstadoOrden>(StringComparer.OrdinalIgnoreCase)
        {
            ["EN_PREPARACION"] = EstadoOrden.EN_PREPARACION,
            ["EN_TRANSITO"] = EstadoOrden.EN_TRANSITO,
            [EstadoListaParaRecoger] = EstadoOrden.LISTA_PARA_RECOGER,
            ["ENTREGADA"] = EstadoOrden.ENTREGADA
        };

    /// <summary>Estados de despacho conocidos que no tienen equivalente en la orden.</summary>
    private static readonly IReadOnlySet<string> EstadosDespachoSinEquivalente =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ORIGEN_PENDIENTE",
            "RECOGIDA_TIENDA",
            "GUIA_SOLICITADA",
            "GUIA_ASIGNADA",
            "GUIA_PENDIENTE",
            "INCIDENCIA",
            "ANULADA"
        };

    private readonly IRegistroMensajesProcesados _registro;
    private readonly IRepositorioOrdenes _ordenes;
    private readonly IOutboxWriter _outbox;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly ILogger<ManejadorEventosDespacho> _logger;

    public ManejadorEventosDespacho(
        IRegistroMensajesProcesados registro,
        IRepositorioOrdenes ordenes,
        IOutboxWriter outbox,
        IUnidadDeTrabajo unidadDeTrabajo,
        ILogger<ManejadorEventosDespacho> logger)
    {
        _registro = registro;
        _ordenes = ordenes;
        _outbox = outbox;
        _unidadDeTrabajo = unidadDeTrabajo;
        _logger = logger;
    }

    public async Task<ResultadoEventoDespacho> ManejarAsync(EventoDespachoRecibido evento, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (await _registro.YaProcesadoAsync(evento.MessageId, cancelacion))
        {
            _logger.LogInformation(
                "Mensaje de Despacho duplicado {MessageId} ({TipoEvento}); se hace ack sin aplicar.",
                evento.MessageId,
                evento.TipoEvento);
            return ResultadoEventoDespacho.Duplicado;
        }

        if (!EstadoOrdenPorEstadoDespacho.TryGetValue(evento.EstadoDespacho, out var estadoDestino))
        {
            if (EstadosDespachoSinEquivalente.Contains(evento.EstadoDespacho))
            {
                _logger.LogInformation(
                    "Despacho informo {EstadoDespacho} para la orden {OrdenId} ({MessageId}); no tiene equivalente en la orden, solo se registra.",
                    evento.EstadoDespacho,
                    evento.OrdenId,
                    evento.MessageId);
            }
            else
            {
                _logger.LogWarning(
                    "Estado de despacho desconocido {EstadoDespacho} para la orden {OrdenId} ({MessageId}); se hace ack sin aplicar.",
                    evento.EstadoDespacho,
                    evento.OrdenId,
                    evento.MessageId);
            }

            return ResultadoEventoDespacho.Ignorado;
        }

        var orden = await _ordenes.ObtenerPorIdAsync(evento.OrdenId, cancelacion);

        if (orden is null)
        {
            _logger.LogWarning(
                "Evento de Despacho {TipoEvento} para orden {OrdenId} no encontrada; se confirma el mensaje.",
                evento.TipoEvento,
                evento.OrdenId);
            return ResultadoEventoDespacho.Ignorado;
        }

        var ruta = CalcularRuta(orden, estadoDestino, out var descarte);

        if (ruta is null)
        {
            _logger.Log(
                descarte!.Value.Nivel,
                "Evento de Despacho {TipoEvento} ({MessageId}) sobre la orden {OrdenId} en {EstadoOrden}: {Motivo}; se hace ack sin aplicar.",
                evento.TipoEvento,
                evento.MessageId,
                orden.Id,
                orden.Estado,
                descarte.Value.Texto);
            return ResultadoEventoDespacho.Ignorado;
        }

        // La marca de idempotencia se agrega antes de las transiciones: se persiste
        // en el mismo SaveChangesAsync que ellas y las filas de outbox.
        _registro.Registrar(evento.MessageId, evento.TipoEvento);

        foreach (var (estado, esIntermedio) in ruta)
        {
            var motivo = esIntermedio
                ? $"Transicion intermedia implicita: Despacho informo {evento.EstadoDespacho}"
                : evento.Detalle ?? $"Despacho informo {evento.EstadoDespacho}";

            var transicion = orden.TransicionarA(estado, motivo, PasoSaga.DESPACHO);

            // Evento 2: toda transicion posterior a la creacion lo emite.
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
        }

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Despacho informo {EstadoDespacho} para la orden {OrdenId}; orden llevada a {EstadoOrden} ({Transiciones} transicion(es)).",
            evento.EstadoDespacho,
            orden.Id,
            orden.Estado,
            ruta.Count);

        return ResultadoEventoDespacho.Aplicado;
    }

    /// <summary>
    /// Arma los estados que la orden debe recorrer para llegar a
    /// <paramref name="estadoDestino" />, o null (con el motivo) si no corresponde
    /// aplicar nada. La secuencia del tramo es siempre DESPACHO_SOLICITADO,
    /// EN_PREPARACION, etapa de salida y ENTREGADA; la etapa de salida es la que
    /// reporta Despacho si el destino es una de las dos, y si no la que corresponde
    /// al metodo de entrega de la orden. Cada paso se valida contra la maquina de
    /// estados, que sigue siendo la unica autoridad.
    /// </summary>
    private static IReadOnlyList<(EstadoOrden Estado, bool EsIntermedio)>? CalcularRuta(
        Orden orden,
        EstadoOrden estadoDestino,
        out (LogLevel Nivel, string Texto)? descarte)
    {
        descarte = null;

        var etapaSalida = estadoDestino is EstadoOrden.EN_TRANSITO or EstadoOrden.LISTA_PARA_RECOGER
            ? estadoDestino
            : orden.MetodoEntrega == MetodoEntrega.DOMICILIO
                ? EstadoOrden.EN_TRANSITO
                : EstadoOrden.LISTA_PARA_RECOGER;

        var tramo = new[]
        {
            EstadoOrden.DESPACHO_SOLICITADO,
            EstadoOrden.EN_PREPARACION,
            etapaSalida,
            EstadoOrden.ENTREGADA
        };

        var indiceActual = Array.IndexOf(tramo, orden.Estado);
        var indiceDestino = Array.IndexOf(tramo, estadoDestino);

        if (indiceActual < 0)
        {
            descarte = orden.Estado is EstadoOrden.DEVOLUCION_EN_PROCESO or EstadoOrden.DEVUELTA
                ? (LogLevel.Information, $"la orden ya paso {estadoDestino}")
                : (LogLevel.Warning, $"la orden no esta en el tramo de entrega y no puede pasar a {estadoDestino}");
            return null;
        }

        if (indiceActual >= indiceDestino)
        {
            descarte = (LogLevel.Information, $"la orden ya esta en {estadoDestino} o paso ese estado");
            return null;
        }

        var ruta = new List<(EstadoOrden Estado, bool EsIntermedio)>();
        for (var i = indiceActual + 1; i <= indiceDestino; i++)
        {
            if (!MaquinaEstadosOrden.EsTransicionValida(tramo[i - 1], tramo[i]))
            {
                descarte = (LogLevel.Warning, $"la maquina de estados no permite {tramo[i - 1]} -> {tramo[i]}");
                return null;
            }

            ruta.Add((tramo[i], EsIntermedio: i < indiceDestino));
        }

        return ruta;
    }
}

/// <summary>
/// Datos ya interpretados de un evento de Despacho que llega por la cola
/// <c>ordenes.despacho</c>. El consumidor arma este registro a partir del sobre y
/// su <c>data</c>; el manejador solo depende de estos campos, no del transporte.
/// </summary>
/// <param name="MessageId">Campo <c>eventId</c> del sobre. Ancla de idempotencia.</param>
/// <param name="TransactionId">Campo <c>transactionId</c> del sobre.</param>
/// <param name="TipoEvento">Campo <c>eventType</c> del sobre.</param>
/// <param name="OrdenId">Orden a la que pertenece el despacho.</param>
/// <param name="EstadoDespacho">
/// Estado de despacho informado, como texto (<c>DespachoEstadoActualizado.estadoNuevo</c>;
/// <c>LISTA_PARA_RECOGER</c> para <c>PedidoListoParaRecoger</c>).
/// </param>
/// <param name="Detalle">Detalle del transportista, si lo hay; alimenta el motivo de la transicion.</param>
public sealed record EventoDespachoRecibido(
    string MessageId,
    string TransactionId,
    string TipoEvento,
    string OrdenId,
    string EstadoDespacho,
    string? Detalle);

/// <summary>Desenlace de aplicar un evento de Despacho a la orden.</summary>
public enum ResultadoEventoDespacho
{
    /// <summary>Se aplico al menos una transicion y se encolaron los eventos derivados.</summary>
    Aplicado,

    /// <summary>El messageId ya estaba registrado: no se aplico nada.</summary>
    Duplicado,

    /// <summary>
    /// Estado sin equivalente en la orden, evento fuera de orden (la orden ya paso
    /// ese estado) u orden inexistente: se hace ack sin reencolar.
    /// </summary>
    Ignorado
}
