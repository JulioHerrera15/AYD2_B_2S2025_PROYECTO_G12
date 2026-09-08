using System.Text.Json;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Caso de uso de creacion y confirmacion de ordenes. La confirmacion es el paso
/// FISCAL de la saga: mueve la orden de CONFIRMADA_PENDIENTE_FISCAL a CONFIRMADA,
/// registra la transicion y deja el evento en el outbox, todo en una sola
/// transaccion de base de datos.
/// </summary>
public sealed class ServicioConfirmacionOrden
{
    private const PasoSaga PasoDeConfirmacion = PasoSaga.FISCAL;

    private readonly IRepositorioOrdenes _ordenes;
    private readonly IRepositorioMensajesSalientes _mensajes;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;

    public ServicioConfirmacionOrden(
        IRepositorioOrdenes ordenes,
        IRepositorioMensajesSalientes mensajes,
        IUnidadDeTrabajo unidadDeTrabajo)
    {
        _ordenes = ordenes;
        _mensajes = mensajes;
        _unidadDeTrabajo = unidadDeTrabajo;
    }

    /// <summary>
    /// Crea una orden en el estado inicial CREADA y la persiste. Si no se envia un
    /// id, se deriva del numero de orden.
    /// </summary>
    public async Task<OrdenDto> CrearOrdenAsync(CrearOrdenRequest solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var lineas = (solicitud.Lineas ?? Array.Empty<CrearLineaOrdenRequest>())
            .Select(linea => new LineaOrden(linea.Sku, linea.Descripcion, linea.Cantidad, linea.PrecioUnitario))
            .ToList();

        var id = string.IsNullOrWhiteSpace(solicitud.Id)
            ? $"ORD-{DateTime.UtcNow:yyyy}-{solicitud.NumeroOrden}"
            : solicitud.Id!;

        var orden = new Orden(
            id,
            solicitud.NumeroOrden,
            solicitud.ClienteId,
            solicitud.Canal,
            solicitud.MetodoEntrega,
            solicitud.Moneda,
            lineas,
            solicitud.Impuestos,
            solicitud.UbicacionSolicitadaId);

        await _ordenes.AgregarAsync(orden, cancelacion);
        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return OrdenDto.Desde(orden);
    }

    /// <summary>
    /// Confirma una orden. Valida la transicion contra la maquina de estados: si es
    /// invalida deja subir <see cref="ExcepcionTransicionInvalida" /> sin escribir
    /// nada. Si la orden ya esta en CONFIRMADA, no escribe nada y responde como
    /// exitosa (idempotencia).
    /// </summary>
    public async Task<ConfirmacionOrdenDto> ConfirmarOrdenAsync(
        string ordenId,
        string? motivo,
        CancellationToken cancelacion)
    {
        var orden = await _ordenes.ObtenerPorIdAsync(ordenId, cancelacion)
            ?? throw new ExcepcionOrdenNoEncontrada(ordenId);

        if (orden.Estado == EstadoOrden.CONFIRMADA)
        {
            return new ConfirmacionOrdenDto(OrdenDto.Desde(orden), YaConfirmada: true);
        }

        // Puede lanzar ExcepcionTransicionInvalida; en ese caso no se escribe nada.
        var transicion = orden.TransicionarA(EstadoOrden.CONFIRMADA, motivo, PasoDeConfirmacion);

        var evento = OrdenEstadoCambiadoEvento.Crear(
            transactionId: EventContext.TransactionId ?? Guid.NewGuid().ToString(),
            aggregateId: orden.Id,
            sequence: transicion.Secuencia,
            data: new OrdenEstadoCambiadoData(
                OrdenId: orden.Id,
                NumeroOrden: orden.NumeroOrden,
                EstadoAnterior: transicion.EstadoAnterior,
                EstadoNuevo: transicion.EstadoNuevo,
                Secuencia: transicion.Secuencia,
                PasoSaga: transicion.PasoSaga,
                Motivo: transicion.Motivo,
                OcurridoEn: transicion.CambiadoEn),
            causationId: EventContext.CausationId);

        var payload = JsonSerializer.Serialize(evento, SgoJsonSerializerOptions.Default);
        var mensaje = new MensajeSaliente(evento.EventType, orden.Id, transicion.Secuencia, payload);

        await _mensajes.AgregarAsync(mensaje, cancelacion);

        // Un unico SaveChanges: estado de la orden + transicion + mensaje saliente
        // se persisten en la misma transaccion.
        await _unidadDeTrabajo.GuardarCambiosAsync(cancelacion);

        return new ConfirmacionOrdenDto(OrdenDto.Desde(orden), YaConfirmada: false);
    }
}
