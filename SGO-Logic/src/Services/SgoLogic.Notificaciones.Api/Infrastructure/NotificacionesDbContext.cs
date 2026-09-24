using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;

namespace SgoLogic.Notificaciones.Api.Infrastructure;

public sealed class NotificacionesDbContext : DbContext
{
    public NotificacionesDbContext(DbContextOptions<NotificacionesDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notificaciones");
        // Solo necesita la tabla de deduplicacion de mensajes procesados
        // (provista por BuildingBlocks, no por migraciones propias del assembly)
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
    }
}
