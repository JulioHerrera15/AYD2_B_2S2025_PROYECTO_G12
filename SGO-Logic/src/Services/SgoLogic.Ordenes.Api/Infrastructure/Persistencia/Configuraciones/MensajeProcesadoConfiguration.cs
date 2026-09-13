using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Mapea la tabla de deduplicacion <c>ordenes.mensaje_procesado</c>. El indice
/// unico sobre <c>message_id</c> es lo que hace atomica la deduplicacion: dos
/// entregas del mismo eventId chocan en la base, no en memoria.
/// </summary>
public sealed class MensajeProcesadoConfiguration : IEntityTypeConfiguration<MensajeProcesado>
{
    public void Configure(EntityTypeBuilder<MensajeProcesado> builder)
    {
        builder.ToTable("mensaje_procesado");

        builder.HasKey(mensaje => mensaje.Id);

        builder.Property(mensaje => mensaje.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(mensaje => mensaje.MessageId)
            .HasColumnName("message_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(mensaje => mensaje.MessageId)
            .IsUnique()
            .HasDatabaseName("ix_mensaje_procesado_message_id");

        builder.Property(mensaje => mensaje.TipoEvento)
            .HasColumnName("tipo_evento")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(mensaje => mensaje.Fecha)
            .HasColumnName("fecha")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();
    }
}
