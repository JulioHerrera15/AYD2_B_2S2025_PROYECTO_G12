using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Sample.Api.Domain;

namespace SgoLogic.Sample.Api.Infrastructure;

/// <summary>
/// DbContext de este servicio. Cada servicio tiene el suyo, contra su propia base
/// de datos: nadie escribe tablas en la base de otro contexto. Incluye la tabla
/// outbox propia (nunca centralizada): este servicio solo produce eventos, no
/// consume, asi que no trae mensajes_procesados.
/// </summary>
public sealed class SampleApiDbContext : DbContext
{
    public SampleApiDbContext(DbContextOptions<SampleApiDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<ItemEjemplo> ItemsEjemplo => Set<ItemEjemplo>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SampleApiDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}