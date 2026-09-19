using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia;

/// <summary>
/// DbContext propio del contexto Despacho, contra su propia base (sgo_despacho) y su
/// propio esquema (ADR-006). Trae outbox porque publica eventos, y
/// mensajes_procesados porque consume los de Ordenes.
/// </summary>
public sealed class DespachoDbContext : DbContext
{
    public const string EsquemaPorDefecto = "despacho";

    public DespachoDbContext(DbContextOptions<DespachoDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<Domain.Despacho> Despachos => Set<Domain.Despacho>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EsquemaPorDefecto);

        modelBuilder.HasSequence<long>("despacho_correlativo", EsquemaPorDefecto);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DespachoDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());

        // Igual que en los demas contextos: la tabla outbox vive en public porque
        // OutboxPublisher la barre sin calificar esquema.
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", schema: "public");
    }
}
