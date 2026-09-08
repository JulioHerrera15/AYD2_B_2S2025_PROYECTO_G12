using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

[Collection("integracion")]
public sealed class BrokerCaidoTests
{
    private readonly IntegracionFixture _fixture;

    public BrokerCaidoTests(IntegracionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Con_el_broker_caido_el_mensaje_permanece_pendiente_y_se_publica_al_recuperarse()
    {
        var routingKey = $"test.brokercaido.{Guid.NewGuid():N}";
        var configuracion = IntegracionServicios.CrearConfiguracion(
            _fixture.RabbitMq.GetConnectionString(), "test-brokercaido", pollingIntervalMs: 300);

        await using var proveedor = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);
        var conexion = new ConexionRabbitMq(configuracion);

        var opciones = IntegracionServicios.CrearOpcionesOutbox("test-brokercaido", pollingIntervalMs: 300);
        var publicador = new OutboxPublisher<TestDbContext>(
            proveedor.GetRequiredService<IServiceScopeFactory>(),
            conexion,
            Options.Create(opciones),
            IntegracionServicios.CrearLogger<OutboxPublisher<TestDbContext>>());

        string eventId;
        await using (var contexto = IntegracionServicios.CrearDbContext(_fixture.Postgres.GetConnectionString()))
        {
            var mensaje = new OutboxMessage(
                Guid.NewGuid().ToString(),
                "BrokerCaido.Probado",
                "{}",
                Guid.NewGuid().ToString(),
                TopologiaMensajeria.ExchangeEventos,
                routingKey,
                "BrokerCaido",
                Guid.NewGuid().ToString());

            eventId = mensaje.EventId;
            contexto.OutboxMessages.Add(mensaje);
            await contexto.SaveChangesAsync();
        }

        await _fixture.RabbitMq.StopAsync();

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            // Deja correr varios ciclos de polling con el broker caido.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(EstadoOutbox.Pendiente, await ObtenerEstadoAsync(eventId));

            await _fixture.RabbitMq.StartAsync();

            // RabbitMq.StartAsync() vuelve cuando el contenedor arranca, no
            // necesariamente cuando el broker ya acepta conexiones AMQP: dar
            // margen generoso antes de declarar el intento fallido.
            var limite = DateTime.UtcNow.AddSeconds(60);
            var estado = EstadoOutbox.Pendiente;
            while (estado != EstadoOutbox.Publicado && DateTime.UtcNow < limite)
            {
                await Task.Delay(300);
                estado = await ObtenerEstadoAsync(eventId);
            }

            Assert.Equal(EstadoOutbox.Publicado, estado);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
            conexion.Dispose();
        }
    }

    private async Task<EstadoOutbox> ObtenerEstadoAsync(string eventId)
    {
        await using var contexto = IntegracionServicios.CrearDbContext(_fixture.Postgres.GetConnectionString());
        return await contexto.OutboxMessages.Where(m => m.EventId == eventId).Select(m => m.Estado).SingleAsync();
    }
}