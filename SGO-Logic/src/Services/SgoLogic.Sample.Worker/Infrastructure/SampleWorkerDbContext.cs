using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.Sample.Worker.Infrastructure;

/// <summary>
/// DbContext de este servicio, contra su propia base: nadie escribe tablas en la
/// base de otro contexto. Trae ambas tablas transversales porque este servicio
/// consume (mensajes_procesados) y tambien publica un evento derivado
/// (outbox_messages).
/// </summary>
public sealed class SampleWorkerDbContext : DbContext
{
    public SampleWorkerDbContext(DbContextOptions<SampleWorkerDbContext> opciones) : base(opciones)
    {
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
    }
}