using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

[Collection("integracion")]
public sealed class OutboxRollbackTests
{
    private readonly IntegracionFixture _fixture;

    public OutboxRollbackTests(IntegracionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Rollback_de_la_transaccion_de_negocio_no_deja_fila_de_outbox_huerfana()
    {
        var configuracion = IntegracionServicios.CrearConfiguracion(_fixture.RabbitMq.GetConnectionString(), "test-rollback");
        await using var proveedor = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);

        await using var contexto = proveedor.GetRequiredService<TestDbContext>();
        var outboxWriter = proveedor.GetRequiredService<IOutboxWriter>();

        var item = new ItemPrueba("se-va-a-revertir");

        await using (var transaccion = await contexto.Database.BeginTransactionAsync())
        {
            contexto.ItemsPrueba.Add(item);
            outboxWriter.Encolar(
                "ItemPrueba.Creado",
                "ItemPrueba",
                item.Id.ToString(),
                sequence: 1,
                exchange: TopologiaMensajeria.ExchangeEventos,
                routingKey: "test.item-prueba.creado",
                data: new { item.Nombre });

            // Misma transaccion, ambas escrituras llegan a la base...
            await contexto.SaveChangesAsync();

            // ...pero algo aborta la transaccion de negocio antes de comprometerla.
            await transaccion.RollbackAsync();
        }

        await using var verificacion = IntegracionServicios.CrearDbContext(_fixture.Postgres.GetConnectionString());

        Assert.False(await verificacion.ItemsPrueba.AnyAsync(i => i.Id == item.Id));
        Assert.False(await verificacion.OutboxMessages.AnyAsync(m => m.AggregateId == item.Id.ToString()));
    }
}