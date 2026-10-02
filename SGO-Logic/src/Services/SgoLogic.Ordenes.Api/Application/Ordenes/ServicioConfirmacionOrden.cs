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
    private readonly IClienteCarrito _carrito;
    private readonly ICatalogoProductos _catalogo;

    public ServicioConfirmacionOrden(
        IRepositorioOrdenes ordenes,
        IOutboxWriter outbox,
        IUnidadDeTrabajo unidadDeTrabajo,
        IGeneradorCorrelativoOrden generadorCorrelativo,
        PoliticaMediosPago politicaMediosPago,
        IClienteCarrito carrito,
        ICatalogoProductos catalogo)
    {
        _ordenes = ordenes;
        _outbox = outbox;
        _unidadDeTrabajo = unidadDeTrabajo;
        _generadorCorrelativo = generadorCorrelativo;
        _politicaMediosPago = politicaMediosPago;
        _carrito = carrito;
        _catalogo = catalogo;
    }

    /// <summary>
    /// Compara cada linea con el catalogo de Inventario. Lanza
    /// <see cref="ExcepcionPrecioNoValido" /> con el primer SKU que no exista, este
    /// inactivo o no tenga el precio vigente; deja subir
    /// <see cref="ExcepcionCatalogoNoDisponible" /> si Inventario no responde.
    /// </summary>
    private async Task ValidarPreciosAsync(IReadOnlyList<CrearLineaOrdenRequest> lineas, CancellationToken cancelacion)
    {
        foreach (var linea in lineas)
        {
            if (string.IsNullOrWhiteSpace(linea.Sku))
            {
                // La entidad LineaOrden rechaza el SKU vacio con su propio mensaje (400).
                continue;
            }

            var sku = linea.Sku.Trim();
            var producto = await _catalogo.ObtenerAsync(sku, cancelacion);

            if (producto is null || !producto.Activo)
            {
                throw new ExcepcionPrecioNoValido(sku, $"El producto '{sku}' no existe o no esta disponible para la venta.");
            }

            if (decimal.Round(linea.PrecioUnitario, 2) != producto.Precio)
            {
                throw new ExcepcionPrecioNoValido(
                    sku,
                    $"El precio unitario de '{sku}' no coincide con el precio vigente del catalogo.",
                    producto.Precio);
            }
        }
    }

    /// <summary>
    /// Crea una orden en el estado inicial CREADA, la persiste y emite OrdenCreada
    /// (routing key ordenes.orden.creada, siempre con sequence 1). El identificador
    /// y el numero de orden los genera el servidor a partir de la secuencia
    /// <c>ordenes.orden_correlativo</c>: id ORD-&lt;anio UTC&gt;-&lt;correlativo de
    /// 6 digitos&gt;. Rechaza con <see cref="ExcepcionMedioPagoNoHabilitado" /> un
    /// medio de pago que no este habilitado para el canal y con
    /// <see cref="ExcepcionTokenMedioPagoRequerido" /> una orden con TARJETA sin
    /// token. OrdenCreada no lleva el token: la orden lo conserva hasta emitir
    /// CobroSolicitado.
    /// </summary>
    public async Task<OrdenDto> CrearOrdenAsync(CrearOrdenRequest solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (!Enum.TryParse<MedioPago>(solicitud.MedioPago, ignoreCase: true, out var medioPago))
        {
            throw new ArgumentException($"El medio de pago '{solicitud.MedioPago}' no es valido. Valores permitidos: {string.Join(", ", Enum.GetNames<MedioPago>())}.", nameof(solicitud.MedioPago));
        }

        if (!_politicaMediosPago.EstaHabilitado(solicitud.Canal, medioPago))
        {
            throw new ExcepcionMedioPagoNoHabilitado(solicitud.Canal, medioPago);
        }

        // ADR-010: la tarjeta se cobra con el token que emitio la boveda de Pagos.
        // Aqui solo se exige y se conserva hasta CobroSolicitado; ningun dato de
        // tarjeta llega a este servicio. Para los medios sin token se ignora.
        var requiereToken = _politicaMediosPago.RequiereToken(medioPago);
        if (requiereToken && string.IsNullOrWhiteSpace(solicitud.TokenMedioPago))
        {
            throw new ExcepcionTokenMedioPagoRequerido(medioPago);
        }

        var lineasSolicitadas = solicitud.Lineas ?? Array.Empty<CrearLineaOrdenRequest>();

        // B1: el precio no lo decide quien llama. Cada linea se compara con el catalogo
        // de Inventario antes de crear nada; un SKU desconocido, inactivo o con otro
        // precio rechaza la orden completa (422), e Inventario caido la rechaza (503).
        await ValidarPreciosAsync(lineasSolicitadas, cancelacion);

        var lineas = lineasSolicitadas
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
            medioPago,
            lineas,
            solicitud.Impuestos,
            solicitud.UbicacionSolicitadaId,
            receptorCui: receptorCui,
            direccionEntrega: solicitud.DireccionEntrega,
            tokenMedioPago: requiereToken ? solicitud.TokenMedioPago : null,
            carritoId: solicitud.CarritoId);

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
                DireccionEntrega: orden.DireccionEntrega,
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
    ///
    /// Este es el punto donde la orden pasa a consumir el carrito que la origino:
    /// si el cliente solo crea la orden (CREADA) pero nunca llega a confirmarla,
    /// su carrito debe seguir intacto (por eso el vaciado no vive en
    /// CrearOrdenAsync). Se vacia sin condicionarlo al desenlace del cobro, que
    /// llega despues de forma asincrona: la orden ya reservo inventario a partir
    /// de ese carrito aunque el pago quede pendiente.
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

        if (!sinCambios)
        {
            // Solo en la confirmacion real, no en un reintento idempotente sobre
            // una orden que ya estaba confirmada (evita vaciar el carrito de nuevo
            // sin necesidad en cada reintento del cliente HTTP).
            await _carrito.VaciarAsync(orden.ClienteId, cancelacion);
        }

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
    /// <param name="encadenadas">
    /// Transiciones que la saga aplica a continuacion de la principal, en orden,
    /// dentro de la misma transaccion (por ejemplo PAGO_APLICADO seguido de
    /// CONFIRMADA_PENDIENTE_FISCAL). Cada una pasa por el mismo camino que la
    /// principal, asi que valida contra la maquina de estados y encola los mismos
    /// eventos que si se hubiera pedido por separado. No se aplican si la
    /// principal es el camino idempotente; si alguna es invalida no se escribe nada.
    /// </param>
    public async Task<ResultadoTransicionOrdenDto> AplicarTransicionAsync(
        string ordenId,
        EstadoOrden estadoDestino,
        PasoSaga? pasoSaga,
        string? motivo,
        bool esCompensacion,
        CancellationToken cancelacion,
        MotivoLiberacionReserva? motivoLiberacion = null,
        Action<Orden>? antesDeTransicionar = null,
        IReadOnlyList<TransicionEncadenada>? encadenadas = null)
    {
        var (orden, sinCambios) = await AplicarTransicionInternaAsync(
            ordenId,
            estadoDestino,
            pasoSaga,
            motivo,
            esCompensacion,
            cancelacion,
            motivoLiberacion,
            antesDeTransicionar,
            encadenadas);

        return new ResultadoTransicionOrdenDto(OrdenDto.Desde(orden), sinCambios);
    }

    /// <summary>
    /// Cancelacion pedida por el cliente dueno de la orden. Solo procede antes de
    /// que el pedido salga a ruta y fuera de la ventana del cobro:
    /// <list type="bullet">
    ///   <item>Otro cliente: <see cref="ExcepcionOrdenAjena" /> (403).</item>
    ///   <item>COBRO_SOLICITADO o PAGO_APLICADO, o en la ruta de credito
    ///   EVALUACION_CREDITO_SOLICITADA o CREDITO_APROBADO: el resultado del pago esta
    ///   por llegar; <see cref="ExcepcionCancelacionNoPermitida" /> transitoria (409).</item>
    ///   <item>EN_TRANSITO o posterior, o ya cancelada/rechazada:
    ///   <see cref="ExcepcionCancelacionNoPermitida" /> definitiva (409).</item>
    /// </list>
    /// En cualquier otro estado aplica CANCELADA como compensacion, de modo que
    /// <see cref="MapaEventosSagaOrden" /> emite OrdenCancelada (con
    /// RequiereReembolso si ya hubo PAGO_APLICADO) y LiberacionReservaSolicitada
    /// si la orden ya tiene reserva. Si aun no la tiene (RESERVA_SOLICITADA), la
    /// libera <see cref="ManejadorEventosInventario" /> al llegar InventarioReservado.
    /// </summary>
    public async Task<ResultadoTransicionOrdenDto> CancelarPorClienteAsync(
        string ordenId,
        CancelarOrdenRequest solicitud,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(ordenId);

        if (!string.Equals(orden.ClienteId, solicitud.ClienteId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ExcepcionOrdenAjena(ordenId);
        }

        if (EstadosConPagoEnCurso.Contains(orden.Estado))
        {
            throw new ExcepcionCancelacionNoPermitida(ordenId, orden.Estado, pagoEnCurso: true);
        }

        if (!EstadosCancelablesPorCliente.Contains(orden.Estado))
        {
            throw new ExcepcionCancelacionNoPermitida(ordenId, orden.Estado, pagoEnCurso: false);
        }

        var motivo = string.IsNullOrWhiteSpace(solicitud.Motivo)
            ? "Cancelada por el cliente"
            : solicitud.Motivo.Trim();

        var (ordenCancelada, sinCambios) = await AplicarTransicionInternaAsync(
            ordenId,
            EstadoOrden.CANCELADA,
            pasoSaga: null,
            motivo,
            esCompensacion: true,
            cancelacion,
            MotivoLiberacionReserva.CANCELACION_CLIENTE);

        return new ResultadoTransicionOrdenDto(OrdenDto.Desde(ordenCancelada), sinCambios);
    }

    /// <summary>
    /// Estados en los que el pago se esta resolviendo: cancelar ahora competiria con
    /// la respuesta de Pagos o de Credito. Mismo trato para las dos rutas.
    /// </summary>
    private static readonly IReadOnlySet<EstadoOrden> EstadosConPagoEnCurso = new HashSet<EstadoOrden>
    {
        EstadoOrden.COBRO_SOLICITADO,
        EstadoOrden.PAGO_APLICADO,
        EstadoOrden.EVALUACION_CREDITO_SOLICITADA,
        EstadoOrden.CREDITO_APROBADO
    };

    /// <summary>
    /// Estados previos a la salida a ruta desde los que el cliente puede cancelar.
    /// Los de <see cref="EstadosConPagoEnCurso" /> quedan fuera. Una orden a credito
    /// confirmada si se puede cancelar: Credito anula el cargo al consumir OrdenCancelada.
    /// </summary>
    private static readonly IReadOnlySet<EstadoOrden> EstadosCancelablesPorCliente = new HashSet<EstadoOrden>
    {
        EstadoOrden.CREADA,
        EstadoOrden.RESERVA_SOLICITADA,
        EstadoOrden.INVENTARIO_RESERVADO,
        EstadoOrden.PAGO_PENDIENTE,
        EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
        EstadoOrden.CONFIRMADA,
        EstadoOrden.DESPACHO_SOLICITADO,
        EstadoOrden.EN_PREPARACION
    };

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
        orden.CambiarMetodoEntrega(
            solicitud.MetodoEntregaNuevo,
            solicitud.UbicacionRecogidaId,
            solicitud.DireccionEntrega);

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
                DireccionEntrega: orden.DireccionEntrega,
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
        Action<Orden>? antesDeTransicionar = null,
        IReadOnlyList<TransicionEncadenada>? encadenadas = null)
    {
        var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(ordenId);

        if (orden.Estado == estadoDestino)
        {
            return (orden, true);
        }

        antesDeTransicionar?.Invoke(orden);

        // Puede lanzar ExcepcionTransicionInvalida; en ese caso no se escribe nada.
        EncolarTransicion(orden, estadoDestino, pasoSaga, motivo, esCompensacion, motivoLiberacion);

        foreach (var siguiente in encadenadas ?? Array.Empty<TransicionEncadenada>())
        {
            EncolarTransicion(
                orden,
                siguiente.EstadoDestino,
                siguiente.PasoSaga,
                siguiente.Motivo,
                esCompensacion: false,
                motivoLiberacion: null);
        }

        // Un unico SaveChanges: estado de la orden + transiciones + filas de outbox
        // se persisten en la misma transaccion.
        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return (orden, false);
    }

    /// <summary>
    /// Aplica una transicion al agregado y encola sus eventos, sin guardar. Es el
    /// unico camino por el que una transicion llega al outbox: lo usan la
    /// principal y las encadenadas.
    /// </summary>
    private void EncolarTransicion(
        Orden orden,
        EstadoOrden estadoDestino,
        PasoSaga? pasoSaga,
        string? motivo,
        bool esCompensacion,
        MotivoLiberacionReserva? motivoLiberacion)
    {
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

    /// <summary>
    /// Reintento manual de certificacion fiscal. Limpia la bandera de alerta,
    /// re-emite <see cref="CertificacionFiscalSolicitada" /> por outbox (reutilizando
    /// la construccion del evento que ya existe en
    /// <see cref="Eventos.MapaEventosSagaOrden" />) y persiste todo en una sola
    /// transaccion. Solo procede si la orden esta en CONFIRMADA_PENDIENTE_FISCAL y
    /// tiene la bandera de alerta levantada; en cualquier otro caso lanza
    /// <see cref="InvalidOperationException" /> para que el endpoint devuelva 409.
    /// </summary>
    public async Task<ResultadoTransicionOrdenDto> ReintentarCertificacionAsync(
        string ordenId,
        CancellationToken cancelacion)
    {
        var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(ordenId);

        if (orden.Estado != EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL)
        {
            throw new InvalidOperationException(
                $"La orden {ordenId} esta en estado {orden.Estado} y no puede reintentar certificacion fiscal. " +
                $"Debe estar en CONFIRMADA_PENDIENTE_FISCAL.");
        }

        if (!orden.AlertaFiscal)
        {
            throw new InvalidOperationException(
                $"La orden {ordenId} no tiene alerta fiscal levantada. " +
                $"Solo se puede reintentar cuando la certificacion SAT fallo y se levanto la alerta.");
        }

        orden.LimpiarAlertaFiscal();

        _outbox.Encolar(
            CertificacionFiscalSolicitada.EventType,
            CertificacionFiscalSolicitada.AggregateType,
            orden.Id,
            orden.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            CertificacionFiscalSolicitada.RoutingKey,
            new CertificacionFiscalSolicitadaData(
                OrdenId: orden.Id,
                TipoDocumento: TipoDocumentoFiscal.FACTURA,
                DocumentoOrigenId: null,
                Receptor: new ReceptorFiscalData(orden.ReceptorCui, orden.ClienteId, "PENDIENTE", Pais.GT),
                Moneda: orden.Moneda,
                Subtotal: orden.Subtotal,
                Impuestos: orden.Impuestos,
                Total: orden.Total,
                Lineas: orden.Lineas
                    .Select(linea => new CertificacionFiscalLineaData(
                        linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario, 0m))
                    .ToList(),
                SolicitadaEn: DateTime.UtcNow));

        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return new ResultadoTransicionOrdenDto(OrdenDto.Desde(orden), SinCambios: false);
    }
}
