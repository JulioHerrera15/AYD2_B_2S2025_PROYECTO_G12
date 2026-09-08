using Microsoft.EntityFrameworkCore;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Ordenes. Contra su propia base (sgo_ordenes) y su
/// propio esquema (ordenes); ningun otro servicio escribe estas tablas.
/// </summary>
public sealed class OrdenesDbContext : DbContext
{
    public const string EsquemaPorDefecto = "ordenes";

    public OrdenesDbContext(DbContextOptions<OrdenesDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<Orden> Ordenes => Set<Orden>();

    public DbSet<TransicionOrden> Transiciones => Set<TransicionOrden>();

    public DbSet<MensajeSaliente> MensajesSalientes => Set<MensajeSaliente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdenesDbContext).Assembly);
    }
}
