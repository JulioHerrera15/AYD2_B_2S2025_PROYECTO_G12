using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Consumers;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Consumers;

/// <summary>Consumidor de la cola credito.ordenes contra EF InMemory y RabbitMQ inerte.</summary>
public sealed class OrdenesCreditoConsumidorTests : IAsyncLifetime
{
    private readonly string _baseDatos = $"credito-ordenes-{Guid.NewGuid()}";
    private readonly OutboxWriterFalso _outbox = new();
    private readonly SuscriptorMensajesFalso _suscriptor = new();
    private ServiceProvider _proveedor = null!;
    private OrdenesCreditoConsumidor _consumidor = null!;

    public async Task InitializeAsync()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddDbContext<CreditoDbContext>(opciones => opciones.UseInMemoryDatabase(_baseDatos));
        servicios.AddSingleton<IOutboxWriter>(_outbox);
        servicios.AddScoped<IRepositorioCredito, RepositorioCredito>();
        servicios.AddScoped(p => new ServicioPerfilCrediticio(
            p.GetRequiredService<IRepositorioCredito>(), p.GetRequiredService<IOutboxWriter>(), DatosPrueba.Politica(),
            NullLogger<ServicioPerfilCrediticio>.Instance, () => DatosPrueba.Ahora));
        servicios.AddScoped(p => new ServicioEvaluacionCredito(
            p.GetRequiredService<IRepositorioCredito>(), p.GetRequiredService<IOutboxWriter>(), DatosPrueba.Politica(),
            NullLogger<ServicioEvaluacionCredito>.Instance, () => DatosPrueba.Ahora));
        servicios.AddScoped(p => new ServicioAnulacionCargos(
            p.GetRequiredService<IRepositorioCredito>(), p.GetRequiredService<IOutboxWriter>(), DatosPrueba.Politica(),
            NullLogger<ServicioAnulacionCargos>.Instance, () => DatosPrueba.Ahora));
        _proveedor = servicios.BuildServiceProvider();

        using (var alcance = _proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>()
                .AplicarHistorialAsync(DatosPrueba.Cliente2(), CancellationToken.None);
        }

        _outbox.Eventos.Clear();

        _consumidor = new OrdenesCreditoConsumidor(
            _proveedor.GetRequiredService<IServiceScopeFactory>(),
            new ConexionRabbitMqInerte(),
            _suscriptor,
            NullLogger<OrdenesCreditoConsumidor>.Instance);
        await _consumidor.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _consumidor.StopAsync(CancellationToken.None);
        _consumidor.Dispose();
        await _proveedor.DisposeAsync();
    }

    private static byte[] Sobre<TData>(string eventType, string ordenId, TData data, string? eventId = null)
        where TData : notnull =>
        JsonSerializer.SerializeToUtf8Bytes(
            new IntegrationEvent<TData>
            {
                EventId = eventId ?? Guid.NewGuid().ToString(),
                EventType = eventType,
                Sequence = 3,
                TransactionId = Guid.NewGuid().ToString(),
                AggregateType = "Orden",
                AggregateId = ordenId,
                Source = "ordenes",
                Data = data
            },
            SgoJsonSerializerOptions.Default);

    private static byte[] Evaluacion(string ordenId, decimal monto, string? eventId = null) =>
        Sobre(EvaluacionCreditoSolicitada.EventType, ordenId,
            new EvaluacionCreditoSolicitadaData(ordenId, ordenId[^6..], "cliente2@sgo.local", Canal.WEB, Moneda.GTQ, monto, DatosPrueba.Ahora),
            eventId);

    private CreditoDbContext Contexto() =>
        new(new DbContextOptionsBuilder<CreditoDbContext>().UseInMemoryDatabase(_baseDatos).Options);

    [Fact]
    public void Se_suscribe_a_credito_ordenes() => Assert.Equal("credito.ordenes", _suscriptor.Cola);

    [Fact]
    public async Task Evaluacion_aprobada_persiste_cuenta_decision_saldo_y_marca_procesado()
    {
        Assert.True(await _suscriptor.EntregarAsync(Evaluacion("ORD-2026-000201", 1200m)));

        await using var contexto = Contexto();
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, Assert.Single(await contexto.CuentasPorCobrar.ToListAsync()).Estado);
        Assert.Equal(ResultadoDecisionCredito.APROBADA, Assert.Single(await contexto.Decisiones.ToListAsync()).Resultado);
        Assert.Equal(1200m, Assert.Single(await contexto.Lineas.ToListAsync()).SaldoUtilizado);
        Assert.Single(await contexto.MensajesProcesados.ToListAsync());
        Assert.Equal(CreditoAprobado.EventType, Assert.Single(_outbox.Eventos).EventType);
    }

    [Fact]
    public async Task Evaluacion_duplicada_se_descarta_por_eventId()
    {
        var cuerpo = Evaluacion("ORD-2026-000202", 500m, "evt-duplicado");

        Assert.True(await _suscriptor.EntregarAsync(cuerpo));
        Assert.True(await _suscriptor.EntregarAsync(cuerpo));

        await using var contexto = Contexto();
        Assert.Single(await contexto.Decisiones.ToListAsync());
        Assert.Equal(500m, Assert.Single(await contexto.Lineas.ToListAsync()).SaldoUtilizado);
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task OrdenCancelada_a_credito_anula_el_cargo_y_devuelve_el_cupo()
    {
        await _suscriptor.EntregarAsync(Evaluacion("ORD-2026-000203", 800m));

        var cancelada = new OrdenCanceladaData(
            "ORD-2026-000203", "000203", "cliente2@sgo.local", null, true, MotivoCancelacion.CLIENTE, "Cancelada",
            false, null, null, null, null, DatosPrueba.Ahora, MedioPago.CREDITO);
        Assert.True(await _suscriptor.EntregarAsync(Sobre(OrdenCancelada.EventType, "ORD-2026-000203", cancelada)));

        await using var contexto = Contexto();
        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, Assert.Single(await contexto.CuentasPorCobrar.ToListAsync()).Estado);
        Assert.Equal(0m, Assert.Single(await contexto.Lineas.ToListAsync()).SaldoUtilizado);
        Assert.Contains(_outbox.Eventos, e => e.EventType == CargoCreditoAnulado.EventType);
    }
}
