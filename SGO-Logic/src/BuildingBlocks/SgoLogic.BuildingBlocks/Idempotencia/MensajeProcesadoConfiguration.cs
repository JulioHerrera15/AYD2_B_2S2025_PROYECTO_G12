using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SgoLogic.BuildingBlocks.Idempotencia;

/// <summary>
/// Cada servicio que consume eventos aplica esta configuracion a SU PROPIO
/// DbContext con modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration()).
/// </summary>
public sealed class MensajeProcesadoConfiguration : IEntityTypeConfiguration<MensajeProcesado>
{
    public void Configure(EntityTypeBuilder<MensajeProcesado> builder)
    {
        builder.ToTable("mensajes_procesados");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.EventId).HasColumnName("event_id").IsRequired().HasMaxLength(64);
        builder.Property(m => m.NombreServicio).HasColumnName("nombre_servicio").IsRequired().HasMaxLength(100);
        builder.Property(m => m.FechaProceso).HasColumnName("fecha_proceso").IsRequired();

        builder.HasIndex(m => new { m.EventId, m.NombreServicio }).IsUnique();
    }
}