using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Application.Despacho;

/// <summary>
/// Nucleo de HU-011, HU-012, HU-019 y HU-020. Determina desde donde sale un pedido,
/// coordina la guia con LogiExpress y lleva el estado de entrega.
///
/// No llama a <c>SaveChangesAsync</c>: la transaccion la cierra quien llama, para
/// que el cambio de negocio y las filas de outbox queden juntos. Lo usan el
/// consumidor de eventos de Ordenes y los endpoints REST.
/// </summary>
public sealed class ServicioDespacho
{
    private readonly IRepositorioDespachos _despachos;
    private readonly IClienteInventario _inventario;
    private readonly IClienteLogiExpress _logiExpress;
    private readonly IGeneradorCorrelativoDespacho _correlativo;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<ServicioDespacho> _logger;

    public ServicioDespacho(
        IRepositorioDespachos despachos,
        IClienteInventario inventario,
        IClienteLogiExpress logiExpress,
        IGeneradorCorrelativoDespacho correlativo,
        IOutboxWriter outbox,
        ILogger<ServicioDespacho> logger)
    {
        _despachos = despachos;
        _inventario = inventario;
        _logiExpress = logiExpress;
        _correlativo = correlativo;
        _outbox = outbox;
        _logger = logger;
    }

    /// <summary>
    /// Crea el despacho de una orden, resuelve el origen y pide la guia si aplica.
    /// Idempotente por orden: si ya existe, devuelve el que hay sin repetir nada.
    /// </summary>
    public async Task<DespachoDto> SolicitarAsync(SolicitudDespacho solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var existente = await _despachos.ObtenerPorOrdenAsync(solicitud.OrdenId, cancelacion);
        if (existente is not null)
        {
            return DespachoDto.Desde(existente);
        }

        var correlativo = await _correlativo.SiguienteAsync(cancelacion);
        var despacho = Domain.Despacho.Crear(
            $"DES-{DateTime.UtcNow:yyyy}-{correlativo:D6}",
            solicitud.OrdenId,
            solicitud.ReservaId,
            solicitud.MetodoEntrega,
            solicitud.UbicacionRecogidaId,
            solicitud.DireccionEntrega,
            solicitud.Lineas.Select(l => (l.Sku, l.Descripcion, l.Cantidad)));
        despacho.RegistrarCliente(solicitud.ClienteId);

        await _despachos.AgregarAsync(despacho, cancelacion);

        await ResolverOrigenYGuiaAsync(despacho, solicitud.UbicacionOrigenSugeridaId, solicitud.TipoUbicacionOrigenSugerida, cancelacion);

        return DespachoDto.Desde(despacho);
    }

    /// <summary>
    /// Resuelve el origen y, si el pedido va a domicilio, pide la guia. Compartido
    /// entre el alta y la reasignacion de metodo de entrega, para que los dos caminos
    /// no puedan divergir.
    /// </summary>
    private async Task ResolverOrigenYGuiaAsync(
        Domain.Despacho despacho,
        string? ubicacionSugeridaId,
        TipoUbicacion? tipoSugerido,
        CancellationToken cancelacion)
    {
        var (ubicacionId, tipoUbicacion) = await DeterminarOrigenAsync(despacho, ubicacionSugeridaId, tipoSugerido, cancelacion);

        despacho.DeterminarOrigen(ubicacionId, tipoUbicacion);

        _outbox.Encolar(
            OrigenDespachoDeterminado.EventType,
            OrigenDespachoDeterminado.AggregateType,
            despacho.Id,
            despacho.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            OrigenDespachoDeterminado.RoutingKey,
            new OrigenDespachoDeterminadoData(
                despacho.Id,
                despacho.OrdenId,
                despacho.ReservaId,
                despacho.MetodoEntrega,
                ubicacionId,
                tipoUbicacion,
                LineasEvento(despacho),
                DateTime.UtcNow));

        // HU-019: la instruccion de preparacion sale siempre, sea domicilio o
        // recogida. El operador tiene que alistar el pedido en ambos casos.
        EncolarPreparacion(despacho, ubicacionId, tipoUbicacion);

        if (!despacho.RequiereGuia)
        {
            // HU-020: recogida en tienda no pide guia a LogiExpress.
            return;
        }

        await SolicitarGuiaAsync(despacho, ubicacionId, cancelacion);
    }

    /// <summary>
    /// Le pregunta a Inventario desde donde se reservo. La sugerencia de Ordenes solo
    /// se usa como respaldo si Inventario no sabe de la reserva: la verdad esta donde
    /// se descontaron las unidades, no donde se penso que se iban a descontar.
    /// </summary>
    private async Task<(string UbicacionId, TipoUbicacion Tipo)> DeterminarOrigenAsync(
        Domain.Despacho despacho,
        string? ubicacionSugeridaId,
        TipoUbicacion? tipoSugerido,
        CancellationToken cancelacion)
    {
        if (despacho.MetodoEntrega == MetodoEntrega.RECOGIDA_TIENDA &&
            !string.IsNullOrWhiteSpace(despacho.UbicacionRecogidaId))
        {
            // El cliente ya eligio la tienda: de ahi sale.
            return (despacho.UbicacionRecogidaId, TipoUbicacion.TIENDA);
        }

        var reserva = await _inventario.ObtenerUbicacionDeReservaAsync(despacho.OrdenId, cancelacion);
        if (reserva is not null)
        {
            return (reserva.UbicacionId, reserva.TipoUbicacion);
        }

        if (!string.IsNullOrWhiteSpace(ubicacionSugeridaId))
        {
            _logger.LogWarning(
                "Inventario no reporto reserva para la orden {OrdenId}; se usa la ubicacion sugerida {Ubicacion}.",
                despacho.OrdenId,
                ubicacionSugeridaId);

            return (ubicacionSugeridaId, tipoSugerido ?? TipoUbicacion.CENTRO_DISTRIBUCION);
        }

        // Sin reserva ni sugerencia no hay de donde despachar. Se deja subir para que
        // la cola reintente: puede ser que la reserva aun no sea visible.
        throw new InvalidOperationException(
            $"No se pudo determinar el origen de despacho de la orden '{despacho.OrdenId}': " +
            "Inventario no reporto reserva y el evento no trae ubicacion sugerida.");
    }

    /// <summary>
    /// Una sola llamada, timeout corto, y si falla se sigue. El timeout es un
    /// resultado de negocio tolerado (HU-011), no una excepcion que aborte el
    /// consumidor: por eso se atrapa aqui y se traduce a GUIA_PENDIENTE.
    /// </summary>
    private async Task SolicitarGuiaAsync(Domain.Despacho despacho, string ubicacionOrigenId, CancellationToken cancelacion)
    {
        GuiaEmitidaDto? guia = null;
        string motivoFalla = "LogiExpress no emitio la guia.";

        try
        {
            guia = await _logiExpress.SolicitarGuiaAsync(
                new SolicitudGuia(
                    despacho.OrdenId,
                    ubicacionOrigenId,
                    despacho.DireccionEntrega ?? string.Empty,
                    despacho.Lineas.Select(l => new LineaGuiaDto(l.Sku, l.Cantidad, l.Descripcion)).ToList()),
                cancelacion);
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or TimeoutException)
        {
            motivoFalla = $"LogiExpress no respondio a tiempo: {ex.GetType().Name}.";
            _logger.LogWarning(ex, "Timeout de LogiExpress para la orden {OrdenId}; la guia queda pendiente.", despacho.OrdenId);
        }

        if (guia is null)
        {
            despacho.MarcarGuiaPendiente();

            _outbox.Encolar(
                GuiaPendiente.EventType,
                GuiaPendiente.AggregateType,
                despacho.Id,
                despacho.Secuencia,
                TopologiaMensajeria.ExchangeEventos,
                GuiaPendiente.RoutingKey,
                new GuiaPendienteData(despacho.Id, despacho.OrdenId, ubicacionOrigenId, motivoFalla, DateTime.UtcNow));

            return;
        }

        despacho.AsignarGuia(guia.GuiaId, guia.TrackingUrl, guia.Transportista);

        _outbox.Encolar(
            GuiaAsignada.EventType,
            GuiaAsignada.AggregateType,
            despacho.Id,
            despacho.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            GuiaAsignada.RoutingKey,
            new GuiaAsignadaData(
                despacho.Id, despacho.OrdenId, guia.GuiaId, guia.TrackingUrl, guia.Transportista, DateTime.UtcNow));
    }

    private void EncolarPreparacion(Domain.Despacho despacho, string ubicacionId, TipoUbicacion tipoUbicacion) =>
        _outbox.Encolar(
            PreparacionSolicitada.EventType,
            PreparacionSolicitada.AggregateType,
            despacho.Id,
            despacho.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            PreparacionSolicitada.RoutingKey,
            new PreparacionSolicitadaData(
                despacho.Id,
                despacho.OrdenId,
                ubicacionId,
                tipoUbicacion,
                despacho.MetodoEntrega,
                LineasEvento(despacho),
                DateTime.UtcNow));

    /// <summary>HU-012: aplica un estado de entrega. Repetir el mismo estado no emite nada.</summary>
    public async Task<DespachoDto> ActualizarEstadoAsync(
        string ordenId,
        EstadoDespacho nuevoEstado,
        string? detalle,
        DateTime ocurridoEn,
        CancellationToken cancelacion)
    {
        var despacho = await _despachos.ObtenerPorOrdenAsync(ordenId, cancelacion)
            ?? throw new ExcepcionDespachoNoEncontrado(ordenId);

        var anterior = despacho.Estado;

        if (!despacho.ActualizarEstadoEntrega(nuevoEstado, detalle, ocurridoEn))
        {
            // Webhook repetido: ni cambio ni evento.
            return DespachoDto.Desde(despacho);
        }

        _outbox.Encolar(
            DespachoEstadoActualizado.EventType,
            DespachoEstadoActualizado.AggregateType,
            despacho.Id,
            despacho.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            DespachoEstadoActualizado.RoutingKey,
            new DespachoEstadoActualizadoData(
                despacho.Id, despacho.OrdenId, anterior.ToString(), nuevoEstado.ToString(), detalle, ocurridoEn));

        // HU-020: el cliente necesita saber que ya puede pasar por su pedido.
        if (nuevoEstado == EstadoDespacho.LISTA_PARA_RECOGER)
        {
            _outbox.Encolar(
                PedidoListoParaRecoger.EventType,
                PedidoListoParaRecoger.AggregateType,
                despacho.Id,
                despacho.Secuencia,
                TopologiaMensajeria.ExchangeEventos,
                PedidoListoParaRecoger.RoutingKey,
                new PedidoListoParaRecogerData(
                    despacho.Id,
                    despacho.OrdenId,
                    despacho.UbicacionRecogidaId ?? despacho.UbicacionOrigenId ?? string.Empty,
                    ocurridoEn,
                    despacho.ClienteId));
        }

        return DespachoDto.Desde(despacho);
    }

    /// <summary>
    /// HU-011: el cliente cambio el metodo de entrega despues de confirmada la compra.
    /// Si habia guia en curso se anula en LogiExpress antes de reasignar.
    /// </summary>
    public async Task<DespachoDto> CambiarMetodoEntregaAsync(SolicitudCambioMetodo solicitud, CancellationToken cancelacion)
    {
        var despacho = await _despachos.ObtenerPorOrdenAsync(solicitud.OrdenId, cancelacion)
            ?? throw new ExcepcionDespachoNoEncontrado(solicitud.OrdenId);

        if (despacho.MetodoEntrega == solicitud.MetodoEntregaNuevo)
        {
            return DespachoDto.Desde(despacho);
        }

        var guiaAAnular = despacho.GuiaId;

        despacho.ReasignarMetodoEntrega(
            solicitud.MetodoEntregaNuevo,
            solicitud.UbicacionRecogidaId,
            solicitud.DireccionEntrega);

        if (guiaAAnular is not null && solicitud.MetodoEntregaNuevo == MetodoEntrega.RECOGIDA_TIENDA)
        {
            // Que el proveedor no acepte la anulacion no debe abortar el cambio: el
            // despacho ya es de recogida y la guia queda huerfana del lado de ellos.
            try
            {
                await _logiExpress.AnularGuiaAsync(guiaAAnular, cancelacion);
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
            {
                _logger.LogWarning(ex, "No se pudo anular la guia {Guia} en LogiExpress.", guiaAAnular);
            }
        }

        if (solicitud.MetodoEntregaNuevo == MetodoEntrega.RECOGIDA_TIENDA)
        {
            var ubicacion = despacho.UbicacionRecogidaId ?? despacho.UbicacionOrigenId!;
            despacho.DeterminarOrigen(ubicacion, TipoUbicacion.TIENDA);
            EncolarPreparacion(despacho, ubicacion, TipoUbicacion.TIENDA);
        }
        else
        {
            await ResolverOrigenYGuiaAsync(despacho, despacho.UbicacionOrigenId, despacho.TipoUbicacionOrigen, cancelacion);
        }

        return DespachoDto.Desde(despacho);
    }

    /// <summary>La orden se cancelo: se anula el despacho y la guia, para no dejarla huerfana.</summary>
    public async Task AnularPorOrdenCanceladaAsync(string ordenId, CancellationToken cancelacion)
    {
        var despacho = await _despachos.ObtenerPorOrdenAsync(ordenId, cancelacion);
        if (despacho is null || !despacho.Anular())
        {
            return;
        }

        if (despacho.GuiaId is { } guiaId)
        {
            try
            {
                await _logiExpress.AnularGuiaAsync(guiaId, cancelacion);
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
            {
                _logger.LogWarning(ex, "No se pudo anular la guia {Guia} tras cancelar la orden.", guiaId);
            }
        }

        _outbox.Encolar(
            DespachoEstadoActualizado.EventType,
            DespachoEstadoActualizado.AggregateType,
            despacho.Id,
            despacho.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            DespachoEstadoActualizado.RoutingKey,
            new DespachoEstadoActualizadoData(
                despacho.Id, despacho.OrdenId, despacho.Estado.ToString(), nameof(EstadoDespacho.ANULADA),
                "La orden fue cancelada.", DateTime.UtcNow));
    }

    public async Task<DespachoDto> ObtenerPorOrdenAsync(string ordenId, CancellationToken cancelacion)
    {
        var despacho = await _despachos.ObtenerPorOrdenAsync(ordenId, cancelacion)
            ?? throw new ExcepcionDespachoNoEncontrado(ordenId);

        return DespachoDto.Desde(despacho);
    }

    /// <summary>HU-019: cola de preparacion del operador de una ubicacion.</summary>
    public async Task<IReadOnlyList<DespachoDto>> ListarPorUbicacionOrigenAsync(
        string ubicacionOrigenId,
        EstadoDespacho? estado,
        CancellationToken cancelacion)
    {
        var despachos = await _despachos.ListarPorUbicacionOrigenAsync(ubicacionOrigenId, estado, cancelacion);
        return despachos.Select(DespachoDto.Desde).ToList();
    }

    /// <summary>
    /// Panel de despacho: todos los despachos sin importar de que ubicacion salen, o
    /// los de una sola si se indica. El origen lo decide Inventario al reservar, asi
    /// que el panel no puede asumir una lista fija de sedes.
    /// </summary>
    public async Task<IReadOnlyList<DespachoDto>> ListarAsync(
        string? ubicacionOrigenId,
        EstadoDespacho? estado,
        CancellationToken cancelacion)
    {
        var ubicacion = string.IsNullOrWhiteSpace(ubicacionOrigenId) ? null : ubicacionOrigenId.Trim();
        var despachos = await _despachos.ListarAsync(ubicacion, estado, cancelacion);
        return despachos.Select(DespachoDto.Desde).ToList();
    }

    private static IReadOnlyList<LineaDespachoData> LineasEvento(Domain.Despacho despacho) =>
        despacho.Lineas.Select(l => new LineaDespachoData(l.Sku, l.Descripcion, l.Cantidad)).ToList();
}
