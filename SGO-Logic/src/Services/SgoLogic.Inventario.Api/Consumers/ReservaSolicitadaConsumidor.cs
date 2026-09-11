using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;

namespace SgoLogic.Inventario.Api.Consumers;

/// <summary>
/// Consume <c>ordenes.reserva.solicitada</c> (evento 3 de ordenes-eventos-v1,
/// CU-01). Delega el nucleo de HU-001 en <see cref="ServicioReservas" />, que
/// aplica el bloqueo optimista, crea la reserva con TTL y encola el evento de
/// resultado. No llama a SaveChangesAsync: EventConsumerBase guarda las
/// existencias, la reserva, la fila de outbox y la marca de mensaje procesado en
/// una sola transaccion.
///
/// La falta de stock NO nackea el mensaje: es un resultado de negocio
/// (<see cref="ServicioReservas" /> encola ReservaInventarioRechazada). El
/// conflicto de version optimista (ADR-005) si nackea: EventConsumerBase hace el
/// SaveChanges, la DbUpdateConcurrencyException sube, la clase base devuelve false
/// y el mensaje vuelve a la cola quorum para reintentarse.
/// </summary>
public sealed class ReservaSolicitadaConsumidor : EventConsumerBase<InventarioDbContext, ReservaInventarioSolicitadaData>
{
    public ReservaSolicitadaConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<ReservaSolicitadaConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "inventario.ordenes";

    protected override string NombreServicio => "inventario";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReservaInventarioSolicitada.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<ReservaInventarioSolicitadaData> evento,
        InventarioDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var servicio = servicios.GetRequiredService<ServicioReservas>();
        var datos = evento.Data;

        var solicitud = new SolicitudReserva(
            datos.OrdenId,
            datos.MetodoEntrega,
            datos.UbicacionPreferidaId,
            datos.TtlReservaSegundos,
            datos.Lineas
                .Select(linea => new LineaSolicitudReserva(linea.Sku, linea.Cantidad))
                .ToList());

        await servicio.ReservarAsync(solicitud, cancelacion);
    }
}
