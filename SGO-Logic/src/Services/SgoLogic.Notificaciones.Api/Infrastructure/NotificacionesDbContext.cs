using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.Notificaciones.Api.Domain;

namespace SgoLogic.Notificaciones.Api.Infrastructure;

public sealed class NotificacionesDbContext : DbContext
{
    public NotificacionesDbContext(DbContextOptions<NotificacionesDbContext> options) : base(options) { }

    /// <summary>Notificaciones que el proveedor no acepto y esperan reintento (B3).</summary>
    public DbSet<NotificacionPendiente> NotificacionesPendientes => Set<NotificacionPendiente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notificaciones");
        // Tabla de deduplicacion de mensajes procesados (provista por BuildingBlocks).
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());

        modelBuilder.Entity<NotificacionPendiente>(builder =>
        {
            builder.ToTable("notificacion_pendiente");
            builder.HasKey(n => n.Id);
            builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(n => n.EventId).HasColumnName("event_id").HasMaxLength(64).IsRequired();
            builder.Property(n => n.Canal).HasColumnName("canal").HasMaxLength(20).IsRequired();
            builder.Property(n => n.Destinatario).HasColumnName("destinatario").HasMaxLength(254).IsRequired();
            builder.Property(n => n.Asunto).HasColumnName("asunto").HasMaxLength(300).IsRequired();
            builder.Property(n => n.Cuerpo).HasColumnName("cuerpo").IsRequired();
            builder.Property(n => n.Estado).HasColumnName("estado").HasConversion<string>().HasMaxLength(20).IsRequired();
            builder.Property(n => n.Intentos).HasColumnName("intentos").IsRequired();
            builder.Property(n => n.ProximoIntentoEn).HasColumnName("proximo_intento_en").IsRequired();
            builder.Property(n => n.UltimoError).HasColumnName("ultimo_error").HasMaxLength(NotificacionPendiente.LargoMaximoError);
            builder.Property(n => n.CreadaEn).HasColumnName("creada_en").IsRequired();
            builder.Property(n => n.ActualizadaEn).HasColumnName("actualizada_en").IsRequired();
            builder.Property(n => n.EnviadaEn).HasColumnName("enviada_en");

            // Un evento genera a lo sumo una notificacion pendiente: el consumidor ya
            // deduplica por eventId, esto lo garantiza tambien en la base.
            builder.HasIndex(n => n.EventId).IsUnique().HasDatabaseName("ix_notificacion_pendiente_event_id");

            // El worker barre las PENDIENTE por fecha de proximo intento.
            builder.HasIndex(n => new { n.Estado, n.ProximoIntentoEn }).HasDatabaseName("ix_notificacion_pendiente_estado_proximo");
        });
    }
}
