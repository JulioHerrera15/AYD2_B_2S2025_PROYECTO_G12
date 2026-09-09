using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Ordenes. Contra su propia base (sgo_ordenes) y su
/// propio esquema (ordenes); ningun otro servicio escribe estas tablas. Incluye la
/// tabla del outbox transversal de BuildingBlocks (nunca centralizada): este
/// servicio produce eventos, no los consume, asi que no trae mensajes_procesados.
/// </summary>
public sealed class OrdenesDbContext : DbContext
{
    public const string EsquemaPorDefecto = "ordenes";

    public OrdenesDbContext(DbContextOptions<OrdenesDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<Orden> Ordenes => Set<Orden>();

    public DbSet<TransicionOrden> Transiciones => Set<TransicionOrden>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdenesDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        // La tabla outbox vive fuera del esquema propio del contexto: el publicador
        // transversal (OutboxPublisher) la barre con un "SELECT * FROM
        // outbox_messages" sin calificar esquema, igual que en el resto de
        // servicios, que no fijan un esquema por defecto.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
