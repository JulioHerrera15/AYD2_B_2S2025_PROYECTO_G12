using System.Text.RegularExpressions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Casos de uso de creacion y avance de ordenes. La confirmacion es el primer paso
/// de la saga que sale del estado inicial CREADA: segun MaquinaEstadosOrden, la
/// unica transicion de avance desde CREADA es hacia RESERVA_SOLICITADA (paso
/// RESERVA). Todo cambio de negocio encola sus eventos en el outbox transversal
/// (IOutboxWriter) y se persiste con un unico SaveChangesAsync
/// (<see cref="IUnidadDeTrabajo" />): entidad + transicion + filas de outbox en la
/// misma transaccion, como exige PLANTILLA-SERVICIO seccion 6.
/// </summary>
public sealed class ServicioConfirmacionOrden
{
    private const EstadoOrden EstadoDeConfirmacion = EstadoOrden.RESERVA_SOLICITADA;
    private const PasoSaga PasoDeConfirmacion = PasoSaga.RESERVA;

    /// <summary>CUI del receptor cuando el request de creacion no envia uno (HU-005).</summary>
    private const string ReceptorCuiPorDefecto = "CF";

    /// <summary>Acepta exactamente "CF" o una cadena de solo digitos.</summary>
    private static readonly Regex PatronReceptorCui = new(@"^(CF|\d+)$", RegexOptions.Compiled);

    private readonly IRepositorioOrdenes _ordenes;
    private readonly IOutboxWriter _outbox;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly IGeneradorCorrelativoOrden _generadorCorrelativo;
    private readonly PoliticaMediosPago _politicaMediosPago;

    public ServicioConfirmacionOrden(
        IRepositorioOrdenes ordenes,
        IOutboxWriter outbox,
        IUnidadDeTrabajo unidadDeTrabajo,
        IGeneradorCorrelativoOrden generadorCorrelativo,
        PoliticaMediosPago politicaMediosPago)
    {
        _ordenes = ordenes;
        _outbox = outbox;
        _unidadDeTrabajo = unidadDeTrabajo;
        _generadorCorrelativo = generadorCorrelativo;
        _politicaMediosPago = politicaMediosPago;
    }

    /// <summary>
    /// Crea una orden en el estado inicial CREADA, la persiste y emite OrdenCreada
    /// (routing key ordenes.orden.creada, siempre con sequence 1). El identificador
    /// y el numero de orden los genera el servidor a partir de la secuencia
    /// <c>ordenes.orden_correlativo</c>: id ORD-&lt;anio UTC&gt;-&lt;correlativo de
    /// 6 digitos&gt;. Rechaza con <see cref="ExcepcionMedioPagoNoHabilitado" /> un
    /// medio de pago que no este habilitado para el canal.
    /// </summary>
    public async Task<OrdenDto> CrearOrdenAsync(CrearOrdenRequest solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (!_politicaMediosPago.EstaHabilitado(solicitud.Canal, solicitud.MedioPago))
        {
            throw new ExcepcionMedioPagoNoHabilitado(solicitud.Canal, solicitud.MedioPago);
        }

        var lineas = (solicitud.Lineas ?? Array.Empty<CrearLineaOrdenRequest>())
            .Select(linea => new LineaOrden(linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario))
            .ToList();

        var receptorCui = ResolverReceptorCui(solicitud.ReceptorCui);

        var correlativo = await _generadorCorrelativo.SiguienteAsync(cancelacion);
        var numeroOrden = correlativo.ToString("D6");
        var id = $"ORD-{DateTime.UtcNow:yyyy}-{numeroOrden}";

        var orden = new Orden(
            id,
            numeroOrden,
            solicitud.ClienteId,
            solicitud.Canal,
            solicitud.MetodoEntrega,
            solicitud.Moneda,
            solicitud.MedioPago,
            lineas,
            solicitud.Impuestos,
            solicitud.UbicacionSolicitadaId,
            receptorCui: receptorCui);

        await _ordenes.AgregarAsync(orden, cancelacion);

        _outbox.Encolar(
            OrdenCreada.EventType,
            OrdenCreada.AggregateType,
            orden.Id,
            orden.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            OrdenCreada.RoutingKey,
            new OrdenCreadaData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                ClienteId: orden.ClienteId,
                Estado: orden.Estado,
                Canal: orden.Canal,
                MetodoEntrega: orden.MetodoEntrega,
                UbicacionSolicitadaId: orden.UbicacionSolicitadaId,
                Moneda: orden.Moneda,
                Subtotal: orden.Subtotal,
                Impuestos: orden.Impuestos,
                Total: orden.Total,
                Lineas: orden.Lineas
                    .Select(linea => new OrdenCreadaLineaData(
                        linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, linea.Subtotal))
                    .ToList(),
                DireccionEntrega: null,
                CreadaEn: orden.CreadaEn));

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return OrdenDto.Desde(orden);
    }

    /// <summary>
    /// Confirma una orden recien creada: aplica la transicion CREADA -&gt;
    /// RESERVA_SOLICITADA. Valida contra la maquina de estados: si es invalida deja
    /// subir <see cref="ExcepcionTransicionInvalida" /> sin escribir nada. Si la
    /// orden ya esta en el estado destino, no escribe nada y responde como exitosa
    /// (idempotencia).
    /// </summary>
    public async Task<ConfirmacionOrdenDto> ConfirmarOrdenAsync(
        string ordenId,
        string? motivo,
        CancellationToken cancelacion)
    {
        var (orden, sinCambios) = await AplicarTransicionInternaAsync(
            ordenId,
            EstadoDeConfirmacion,
            PasoDeConfirmacion,
            motivo,
            esCompensacion: false,
            cancelacion);

        return new ConfirmacionOrdenDto(OrdenDto.Desde(orden), YaConfirmada: sinCambios);
    }

    /// <summary>
    /// Aplica una transicion arbitraria del ciclo de vida de la orden. Mismo
    /// contrato que la confirmacion: valida contra la maquina de estados, es
    /// idempotente y escribe orden + transicion + eventos del outbox en una sola
    /// transaccion.
    /// </summary>
    /// <param name="antesDeTransicionar">
    /// Mutacion adicional del agregado (por ejemplo, persistir datos que trajo el
    /// evento de entrada) aplicada despues de confirmar que la transicion es real
    /// y antes de <see cref="Orden.TransicionarA" />, para que quede en la misma
    /// transaccion de base de datos. No se invoca si la orden ya esta en el
    /// estado destino (camino idempotente) ni si la transicion resulta invalida.
    /// </param>
    public async Task<ResultadoTransicionOrdenDto> AplicarTransicionAsync(
        string ordenId,
        EstadoOrden estadoDestino,
        PasoSaga? pasoSaga,
        string? motivo,
        bool esCompensacion,
        CancellationToken cancelacion,
        MotivoLiberacionReserva? motivoLiberacion = null,
        Action<Orden>? antesDeTransicionar = null)
    {
        var (orden, sinCambios) = await AplicarTransicionInternaAsync(
            ordenId,
            estadoDestino,
            pasoSaga,
            motivo,
            esCompensacion,
            cancelacion,
            motivoLiberacion,
            antesDeTransicionar);

        return new ResultadoTransicionOrdenDto(OrdenDto.Desde(orden), sinCambios);
    }

    /// <summary>
    /// Cambia el metodo de entrega de una orden no terminal (EV-31) y emite
    /// MetodoEntregaModificado. No es una transicion de estado: la orden sigue
    /// donde estaba. Si el metodo pedido ya es el actual, no escribe nada.
    /// </summary>
    public async Task<ResultadoTransicionOrdenDto> CambiarMetodoEntregaAsync(
        string ordenId,
        CambiarMetodoEntregaRequest solicitud,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(ordenId);

        if (orden.MetodoEntrega == solicitud.MetodoEntregaNuevo)
        {
            return new ResultadoTransicionOrdenDto(OrdenDto.Desde(orden), SinCambios: true);
        }

        var metodoAnterior = orden.MetodoEntrega;

        // Puede lanzar ArgumentException (recogida sin ubicacion) o
        // InvalidOperationException (orden terminal); en ese caso no se escribe nada.
        orden.CambiarMetodoEntrega(solicitud.MetodoEntregaNuevo, solicitud.UbicacionRecogidaId);

        _outbox.Encolar(
            MetodoEntregaModificado.EventType,
            MetodoEntregaModificado.AggregateType,
            orden.Id,
            orden.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            MetodoEntregaModificado.RoutingKey,
            new MetodoEntregaModificadoData(
                OrdenId: orden.Id,
                MetodoEntregaAnterior: metodoAnterior,
                MetodoEntregaNuevo: solicitud.MetodoEntregaNuevo,
                UbicacionRecogidaId: orden.UbicacionSolicitadaId,
                DireccionEntrega: solicitud.DireccionEntrega,
                SolicitadoPor: solicitud.SolicitadoPor,
                ModificadoEn: DateTime.UtcNow));

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return new ResultadoTransicionOrdenDto(OrdenDto.Desde(orden), SinCambios: false);
    }

    private async Task<(Orden Orden, bool SinCambios)> AplicarTransicionInternaAsync(
        string ordenId,
        EstadoOrden estadoDestino,
        PasoSaga? pasoSaga,
        string? motivo,
        bool esCompensacion,
        CancellationToken cancelacion,
        MotivoLiberacionReserva? motivoLiberacion = null,
        Action<Orden>? antesDeTransicionar = null)
    {
        var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(ordenId);

        if (orden.Estado == estadoDestino)
        {
            return (orden, true);
        }

        antesDeTransicionar?.Invoke(orden);

        // Puede lanzar ExcepcionTransicionInvalida; en ese caso no se escribe nada.
        var transicion = orden.TransicionarA(estadoDestino, motivo, pasoSaga, esCompensacion);

        // Evento 2: toda transicion posterior a la creacion lo emite, sin excepcion.
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

        // Evento de saga que corresponda al estado destino (mas liberacion de
        // reserva si la transicion es una compensacion). motivoLiberacion, cuando
        // llega, fija el MotivoLiberacionReserva; si es null se deriva del estado
        // destino como antes.
        MapaEventosSagaOrden.Encolar(orden, transicion, _outbox, motivoLiberacion);

        // Un unico SaveChanges: estado de la orden + transicion + filas de outbox
        // se persisten en la misma transaccion.
        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return (orden, false);
    }

    /// <summary>
    /// "CF" cuando el request no trae CUI. Si trae uno, exige que sea "CF" o solo
    /// digitos; cualquier otra cosa es 400 (ArgumentException, HU-005).
    /// </summary>
    private static string ResolverReceptorCui(string? receptorCui)
    {
        if (string.IsNullOrWhiteSpace(receptorCui))
        {
            return ReceptorCuiPorDefecto;
        }

        if (!PatronReceptorCui.IsMatch(receptorCui))
        {
            throw new ArgumentException(
                "El CUI del receptor debe ser \"CF\" o solo digitos.",
                nameof(receptorCui));
        }

        return receptorCui;
    }
}
