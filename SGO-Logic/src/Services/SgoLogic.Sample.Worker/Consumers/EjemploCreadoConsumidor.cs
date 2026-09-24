using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos;
using SgoLogic.Sample.Worker.Infrastructure;

namespace SgoLogic.Sample.Worker.Consumers;

/// <summary>
/// Consume EjemploCreadoEvento desde sample-api y publica EjemploProcesadoEvento
/// como derivado, tambien por outbox. Demuestra el salto completo: deduplicacion
/// por eventId, y transactionId/causationId propagados sin que este codigo los
/// toque (EventConsumerBase ya puebla EventContext antes de llamar a ManejarAsync;
/// IOutboxWriter los lee de ahi al encolar).
/// </summary>
public sealed class EjemploCreadoConsumidor : EventConsumerBase<SampleWorkerDbContext, EjemploCreadoData>
{
    public EjemploCreadoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<EjemploCreadoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "sample-worker.ejemplo-creado";

    protected override string NombreServicio => "sample-worker";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "sample-api.item-ejemplo.creado")
    ];

    protected override Task ManejarAsync(
        IntegrationEvent<EjemploCreadoData> evento,
        SampleWorkerDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var outboxWriter = servicios.GetRequiredService<IOutboxWriter>();

        outboxWriter.Encolar(
            EjemploProcesadoEvento.TipoEvento,
            EjemploProcesadoEvento.TipoAgregado,
            evento.AggregateId,
            sequence: evento.Sequence + 1,
            exchange: TopologiaMensajeria.ExchangeEventos,
            routingKey: "sample-worker.item-ejemplo.procesado",
            data: new EjemploProcesadoData(evento.Data.Nombre, "procesado por sample-worker"));

        return Task.CompletedTask;
    }
}