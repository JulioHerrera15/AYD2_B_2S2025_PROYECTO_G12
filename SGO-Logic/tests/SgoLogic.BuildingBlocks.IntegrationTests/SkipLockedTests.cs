using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

[Collection("integracion")]
public sealed class SkipLockedTests
{
    private const int CantidadMensajes = 12;

    private readonly IntegracionFixture _fixture;

    public SkipLockedTests(IntegracionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dos_instancias_del_publicador_sobre_la_misma_tabla_no_publican_el_mensaje_dos_veces()
    {
        var routingKey = $"test.skiplocked.{Guid.NewGuid():N}";
        var cola = $"test.skiplocked.cola.{Guid.NewGuid():N}";

        // Sembrar N filas Pendientes directo en la base, sin pasar por IOutboxWriter:
        // lo unico que importa aca es que existan filas para que compitan los dos publicadores.
        await using (var contexto = IntegracionServicios.CrearDbContext(_fixture.Postgres.GetConnectionString()))
        {
            for (var i = 0; i < CantidadMensajes; i++)
            {
                contexto.OutboxMessages.Add(new OutboxMessage(
                    Guid.NewGuid().ToString(),
                    "SkipLocked.Probado",
                    $$"""{"n":{{i}}}""",
                    Guid.NewGuid().ToString(),
                    TopologiaMensajeria.ExchangeEventos,
                    routingKey,
                    "SkipLocked",
                    Guid.NewGuid().ToString()));
            }

            await contexto.SaveChangesAsync();
        }

        // Cola de espia: cuenta cuantas entregas llegan en total. Si SKIP LOCKED
        // fallara, algun mensaje se publicaria dos veces y este contador daria mas de N.
        var conexionEspia = new ConexionRabbitMq(IntegracionServicios.CrearConfiguracion(_fixture.RabbitMq.GetConnectionString(), "espia"));
        using (var canalEspia = conexionEspia.ObtenerConexion().CreateModel())
        {
            DeclaradorTopologia.DeclararEn(canalEspia);
            DeclaradorColas.DeclararCola(canalEspia, cola, [new BindingCola(TopologiaMensajeria.ExchangeEventos, routingKey)]);
        }

        var entregas = new ConcurrentBag<string>();
        var suscriptorEspia = new SuscriptorRabbitMq(conexionEspia);
        using var suscripcionEspia = suscriptorEspia.Suscribir(cola, cuerpo =>
        {
            entregas.Add(System.Text.Encoding.UTF8.GetString(cuerpo.Span));
            return Task.FromResult(true);
        });

        var configuracion = IntegracionServicios.CrearConfiguracion(
            _fixture.RabbitMq.GetConnectionString(), "test-skiplocked", pollingIntervalMs: 150, batchSize: 3);

        await using var proveedorA = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);
        await using var proveedorB = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);

        var conexionA = new ConexionRabbitMq(configuracion);
        var conexionB = new ConexionRabbitMq(configuracion);
        var opciones = IntegracionServicios.CrearOpcionesOutbox("test-skiplocked", pollingIntervalMs: 150, batchSize: 3);

        var publicadorA = new OutboxPublisher<TestDbContext>(
            proveedorA.GetRequiredService<IServiceScopeFactory>(), conexionA,
            Options.Create(opciones),
            IntegracionServicios.CrearLogger<OutboxPublisher<TestDbContext>>());

        var publicadorB = new OutboxPublisher<TestDbContext>(
            proveedorB.GetRequiredService<IServiceScopeFactory>(), conexionB,
            Options.Create(opciones),
            IntegracionServicios.CrearLogger<OutboxPublisher<TestDbContext>>());

        await publicadorA.StartAsync(CancellationToken.None);
        await publicadorB.StartAsync(CancellationToken.None);

        try
        {
            var limite = DateTime.UtcNow.AddSeconds(15);
            while (entregas.Count < CantidadMensajes && DateTime.UtcNow < limite)
            {
                await Task.Delay(200);
            }

            // Da tiempo a una eventual entrega duplicada de llegar antes de contar.
            await Task.Delay(1000);
        }
        finally
        {
            await publicadorA.StopAsync(CancellationToken.None);
            await publicadorB.StopAsync(CancellationToken.None);
            conexionA.Dispose();
            conexionB.Dispose();
            conexionEspia.Dispose();
        }

        Assert.Equal(CantidadMensajes, entregas.Count);
        Assert.Equal(CantidadMensajes, entregas.Distinct().Count());

        await using var verificacion = IntegracionServicios.CrearDbContext(_fixture.Postgres.GetConnectionString());
        var publicados = await verificacion.OutboxMessages
            .CountAsync(m => m.RoutingKey == routingKey && m.Estado == EstadoOutbox.Publicado);

        Assert.Equal(CantidadMensajes, publicados);
    }
}