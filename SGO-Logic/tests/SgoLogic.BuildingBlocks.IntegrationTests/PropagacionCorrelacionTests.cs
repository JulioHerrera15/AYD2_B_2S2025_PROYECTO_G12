using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

[Collection("integracion")]
public sealed class PropagacionCorrelacionTests
{
    private readonly IntegracionFixture _fixture;

    public PropagacionCorrelacionTests(IntegracionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TransactionId_y_causationId_se_propagan_correctamente_a_traves_de_dos_saltos()
    {
        var routingKeyOrigen = $"test.propagacion.origen.{Guid.NewGuid():N}";
        var routingKeyDerivado = $"test.propagacion.derivado.{Guid.NewGuid():N}";
        var colaConsumidor = $"test.propagacion.cola.{Guid.NewGuid():N}";
        var colaEspia = $"test.propagacion.espia.{Guid.NewGuid():N}";

        var configuracion = IntegracionServicios.CrearConfiguracion(
            _fixture.RabbitMq.GetConnectionString(), "test-propagacion", pollingIntervalMs: 200);
        var opciones = IntegracionServicios.CrearOpcionesOutbox("test-propagacion", pollingIntervalMs: 200);

        // --- "sample-api": crea la entidad y encola el evento raiz ---
        await using var proveedorA = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);
        var conexionA = new ConexionRabbitMq(configuracion);
        var publicadorA = new OutboxPublisher<TestDbContext>(
            proveedorA.GetRequiredService<IServiceScopeFactory>(), conexionA,
            Options.Create(opciones), IntegracionServicios.CrearLogger<OutboxPublisher<TestDbContext>>());

        var transactionIdOriginal = Guid.NewGuid().ToString();
        string eventIdOriginal;

        await using (var scope = proveedorA.CreateAsyncScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

            // Simula lo que hace CorrelationMiddleware en una solicitud HTTP nueva:
            // transactionId nuevo, sin causa (accion directa del usuario).
            EventContext.TransactionId = transactionIdOriginal;
            EventContext.CausationId = null;

            var item = new ItemPrueba("propagacion");
            contexto.ItemsPrueba.Add(item);
            outboxWriter.Encolar(
                "ItemPrueba.Creado",
                "ItemPrueba",
                item.Id.ToString(),
                sequence: 1,
                exchange: TopologiaMensajeria.ExchangeEventos,
                routingKey: routingKeyOrigen,
                data: new DatoPrueba("x"));

            await contexto.SaveChangesAsync();

            eventIdOriginal = await contexto.OutboxMessages
                .Where(m => m.AggregateId == item.Id.ToString())
                .Select(m => m.EventId)
                .SingleAsync();

            EventContext.TransactionId = null;
        }

        // --- "sample-worker": consume el evento raiz y publica el derivado ---
        await using var proveedorB = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);
        var conexionB = new ConexionRabbitMq(configuracion);
        var suscriptorB = new SuscriptorRabbitMq(conexionB, NullLogger<SuscriptorRabbitMq>.Instance);
        var publicadorB = new OutboxPublisher<TestDbContext>(
            proveedorB.GetRequiredService<IServiceScopeFactory>(), conexionB,
            Options.Create(opciones), IntegracionServicios.CrearLogger<OutboxPublisher<TestDbContext>>());

        var consumidorB = new PropagadorConsumidor(
            proveedorB.GetRequiredService<IServiceScopeFactory>(), conexionB, suscriptorB,
            IntegracionServicios.CrearLogger<PropagadorConsumidor>(),
            colaConsumidor,
            [new BindingCola(TopologiaMensajeria.ExchangeEventos, routingKeyOrigen)],
            routingKeyDerivado);

        // --- Espia: escucha el evento derivado para inspeccionarlo ---
        var conexionEspia = new ConexionRabbitMq(configuracion);
        using (var canal = conexionEspia.ObtenerConexion().CreateModel())
        {
            DeclaradorTopologia.DeclararEn(canal);
            DeclaradorColas.DeclararCola(canal, colaEspia, [new BindingCola(TopologiaMensajeria.ExchangeEventos, routingKeyDerivado)]);
        }

        IntegrationEvent<DatoPrueba>? recibidoDerivado = null;
        var suscriptorEspia = new SuscriptorRabbitMq(conexionEspia, NullLogger<SuscriptorRabbitMq>.Instance);
        using var suscripcionEspia = suscriptorEspia.Suscribir(colaEspia, cuerpo =>
        {
            recibidoDerivado = JsonSerializer.Deserialize<IntegrationEvent<DatoPrueba>>(cuerpo.Span, SgoJsonSerializerOptions.Default);
            return Task.FromResult(true);
        });

        // Orden importante: la cola de consumidorB (y su binding a routingKeyOrigen)
        // tiene que existir ANTES de que publicadorA empiece a publicar, o el
        // primer intento de publicacion del evento raiz no encuentra a nadie
        // ligado a esa routing key en el exchange topic y el mensaje se pierde sin
        // error (RabbitMQ no falla al publicar a un exchange sin bindings).
        await consumidorB.StartAsync(CancellationToken.None);
        await publicadorB.StartAsync(CancellationToken.None);
        await publicadorA.StartAsync(CancellationToken.None);

        try
        {
            var limite = DateTime.UtcNow.AddSeconds(20);
            while (recibidoDerivado is null && DateTime.UtcNow < limite)
            {
                await Task.Delay(300);
            }

            Assert.NotNull(recibidoDerivado);
            Assert.Equal(transactionIdOriginal, recibidoDerivado!.TransactionId);
            Assert.Equal(eventIdOriginal, recibidoDerivado.CausationId);
            Assert.Equal(2, recibidoDerivado.Sequence);
        }
        finally
        {
            await publicadorA.StopAsync(CancellationToken.None);
            await consumidorB.StopAsync(CancellationToken.None);
            await publicadorB.StopAsync(CancellationToken.None);
            conexionA.Dispose();
            conexionB.Dispose();
            conexionEspia.Dispose();
        }
    }
}