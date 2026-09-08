using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

/// <summary>
/// Consume DatoPrueba y publica un derivado por outbox (igual que
/// EjemploCreadoConsumidor en Sample.Worker), para que el test de propagacion
/// verifique transactionId/causationId a traves de dos saltos reales.
/// </summary>
public sealed class PropagadorConsumidor : EventConsumerBase<TestDbContext, DatoPrueba>
{
    private readonly string _routingKeyDerivado;

    public PropagadorConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger logger,
        string cola,
        IReadOnlyCollection<BindingCola> bindings,
        string routingKeyDerivado)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        Cola = cola;
        Bindings = bindings;
        _routingKeyDerivado = routingKeyDerivado;
    }

    protected override string Cola { get; }

    protected override string NombreServicio => "test-propagador";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; }

    protected override Task ManejarAsync(
        IntegrationEvent<DatoPrueba> evento,
        TestDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var outboxWriter = servicios.GetRequiredService<IOutboxWriter>();

        outboxWriter.Encolar(
            "DatoPrueba.Derivado",
            evento.AggregateType,
            evento.AggregateId,
            sequence: evento.Sequence + 1,
            exchange: TopologiaMensajeria.ExchangeEventos,
            routingKey: _routingKeyDerivado,
            data: new DatoPrueba(evento.Data.Valor + "-derivado"));

        return Task.CompletedTask;
    }
}