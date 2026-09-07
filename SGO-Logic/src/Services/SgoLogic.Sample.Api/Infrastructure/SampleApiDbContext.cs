using Microsoft.EntityFrameworkCore;
using SgoLogic.Sample.Api.Domain;

namespace SgoLogic.Sample.Api.Infrastructure;

/// <summary>
/// DbContext de este servicio. Cada servicio tiene el suyo, contra su propia base
/// de datos: nadie escribe tablas en la base de otro contexto.
/// </summary>
public sealed class SampleApiDbContext : DbContext
{
    public SampleApiDbContext(DbContextOptions<SampleApiDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<ItemEjemplo> ItemsEjemplo => Set<ItemEjemplo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SampleApiDbContext).Assembly);
    }
}