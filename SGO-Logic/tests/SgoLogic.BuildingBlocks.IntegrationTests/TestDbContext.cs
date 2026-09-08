using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

/// <summary>
/// DbContext de prueba que trae las tres tablas relevantes (la de negocio y las
/// dos transversales), igual que cualquier DbContext real de servicio.
/// </summary>
public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<ItemPrueba> ItemsPrueba => Set<ItemPrueba>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ItemPruebaConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
    }
}