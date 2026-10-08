using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Consumers;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Consumers;

/// <summary>
/// El consumidor real (EventConsumerBase) contra EF InMemory, con RabbitMQ inerte y
/// un suscriptor que entrega los mensajes a mano.
/// </summary>
public sealed class HistorialClienteErpConsumidorTests : IAsyncLifetime
{
    private readonly string _baseDatos = $"credito-consumidor-{Guid.NewGuid()}";
    private readonly OutboxWriterFalso _outbox = new();
    private readonly SuscriptorMensajesFalso _suscriptor = new();
    private ServiceProvider _proveedor = null!;
    private HistorialClienteErpConsumidor _consumidor = null!;

    public async Task InitializeAsync()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddDbContext<CreditoDbContext>(opciones => opciones.UseInMemoryDatabase(_baseDatos));
        servicios.AddSingleton<IOutboxWriter>(_outbox);
        servicios.AddScoped<IRepositorioCredito, RepositorioCredito>();
        servicios.AddScoped(proveedor => new ServicioPerfilCrediticio(
            proveedor.GetRequiredService<IRepositorioCredito>(),
            proveedor.GetRequiredService<IOutboxWriter>(),
            DatosPrueba.Politica(),
            NullLogger<ServicioPerfilCrediticio>.Instance,
            () => DatosPrueba.Ahora));
        _proveedor = servicios.BuildServiceProvider();

        _consumidor = new HistorialClienteErpConsumidor(
            _proveedor.GetRequiredService<IServiceScopeFactory>(),
            new ConexionRabbitMqInerte(),
            _suscriptor,
            NullLogger<HistorialClienteErpConsumidor>.Instance);

        await _consumidor.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _consumidor.StopAsync(CancellationToken.None);
        _consumidor.Dispose();
        await _proveedor.DisposeAsync();
    }

    private static byte[] Sobre(HistorialClienteErpActualizadoData datos, string? eventId = null) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new IntegrationEvent<HistorialClienteErpActualizadoData>
            {
                EventId = eventId ?? Guid.NewGuid().ToString(),
                EventType = HistorialClienteErpActualizado.EventType,
                Sequence = 1,
                TransactionId = Guid.NewGuid().ToString(),
                AggregateType = HistorialClienteErpActualizado.AggregateType,
                AggregateId = datos.ClienteId,
                Source = "integracion-erp",
                Data = datos
            },
            SgoJsonSerializerOptions.Default);

    private CreditoDbContext Contexto() =>
        new(new DbContextOptionsBuilder<CreditoDbContext>().UseInMemoryDatabase(_baseDatos).Options);

    [Fact]
    public void Se_suscribe_a_su_cola_propia()
    {
        Assert.Equal("credito.erp", _suscriptor.Cola);
    }

    [Fact]
    public async Task Aplica_el_evento_y_guarda_todo_junto_con_la_marca_de_procesado()
    {
        var ack = await _suscriptor.EntregarAsync(Sobre(DatosPrueba.Cliente2()));

        Assert.True(ack);
        await using var contexto = Contexto();
        var perfil = Assert.Single(await contexto.Perfiles.ToListAsync());
        Assert.Equal(79, perfil.Puntaje);
        Assert.Equal(NivelCredito.BUENO, perfil.Nivel);
        Assert.Equal(3500m, Assert.Single(await contexto.Lineas.ToListAsync()).LimiteAprobado);
        Assert.Single(await contexto.Historiales.ToListAsync());
        Assert.Single(await contexto.MensajesProcesados.ToListAsync());
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task Descarta_un_duplicado_por_eventId_sin_reaplicar()
    {
        var eventId = Guid.NewGuid().ToString();
        var cuerpo = Sobre(DatosPrueba.Cliente1(), eventId);

        Assert.True(await _suscriptor.EntregarAsync(cuerpo));
        Assert.True(await _suscriptor.EntregarAsync(cuerpo));

        await using var contexto = Contexto();
        Assert.Single(await contexto.MensajesProcesados.ToListAsync());
        Assert.Equal(1, Assert.Single(await contexto.Perfiles.ToListAsync()).Version);
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task Un_evento_nuevo_con_fecha_de_corte_vieja_se_marca_procesado_sin_cambiar_el_perfil()
    {
        await _suscriptor.EntregarAsync(Sobre(DatosPrueba.Cliente1()));
        var viejo = DatosPrueba.Cliente4() with { ClienteId = "cliente1@sgo.local", FechaCorte = DatosPrueba.Ahora.AddDays(-3) };

        Assert.True(await _suscriptor.EntregarAsync(Sobre(viejo)));

        await using var contexto = Contexto();
        Assert.Equal(2, (await contexto.MensajesProcesados.ToListAsync()).Count);
        Assert.Equal(98, Assert.Single(await contexto.Perfiles.ToListAsync()).Puntaje);
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task Un_historial_invalido_se_nackea()
    {
        var ack = await _suscriptor.EntregarAsync(Sobre(DatosPrueba.Cliente1() with { PagosTardios = -1 }));

        Assert.False(ack);
        await using var contexto = Contexto();
        Assert.Empty(await contexto.MensajesProcesados.ToListAsync());
    }
}
