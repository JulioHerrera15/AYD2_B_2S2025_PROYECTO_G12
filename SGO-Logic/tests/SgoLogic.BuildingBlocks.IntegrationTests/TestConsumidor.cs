using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

public sealed record DatoPrueba(string Valor);

/// <summary>Cuenta cuantas veces se ejecuto ManejarAsync, para que el test verifique deduplicacion.</summary>
public sealed class ContadorLlamadas
{
    private int _valor;
    public int Valor => _valor;
    public void Incrementar() => Interlocked.Increment(ref _valor);
}

public sealed class TestConsumidor : EventConsumerBase<TestDbContext, DatoPrueba>
{
    private readonly ContadorLlamadas _contador;

    public TestConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger logger,
        ContadorLlamadas contador,
        string cola,
        IReadOnlyCollection<BindingCola> bindings,
        string nombreServicio = "test-consumidor")
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _contador = contador;
        Cola = cola;
        Bindings = bindings;
        NombreServicio = nombreServicio;
    }

    protected override string Cola { get; }

    protected override string NombreServicio { get; }

    protected override IReadOnlyCollection<BindingCola> Bindings { get; }

    protected override Task ManejarAsync(
        IntegrationEvent<DatoPrueba> evento,
        TestDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        _contador.Incrementar();
        return Task.CompletedTask;
    }
}