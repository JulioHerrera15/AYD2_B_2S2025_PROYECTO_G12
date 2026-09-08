using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class MensajeSalienteConfiguration : IEntityTypeConfiguration<MensajeSaliente>
{
    public void Configure(EntityTypeBuilder<MensajeSaliente> builder)
    {
        builder.ToTable("mensaje_saliente");

        builder.HasKey(mensaje => mensaje.Id);

        builder.Property(mensaje => mensaje.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(mensaje => mensaje.TipoEvento)
            .HasColumnName("tipo_evento")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(mensaje => mensaje.AgregadoId)
            .HasColumnName("agregado_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(mensaje => mensaje.Secuencia)
            .HasColumnName("secuencia")
            .IsRequired();

        builder.Property(mensaje => mensaje.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(mensaje => mensaje.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(mensaje => mensaje.Intentos)
            .HasColumnName("intentos")
            .IsRequired();

        builder.Property(mensaje => mensaje.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        // El dominio ya deja PublicadoEn en UTC (MarcarPublicado usa ToUniversalTime).
        builder.Property(mensaje => mensaje.PublicadoEn)
            .HasColumnName("publicado_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(mensaje => mensaje.UltimoError)
            .HasColumnName("ultimo_error")
            .HasColumnType("text");

        // Indice para el publicador por polling: barre pendientes por antiguedad.
        builder.HasIndex(mensaje => new { mensaje.Estado, mensaje.CreadoEn })
            .HasDatabaseName("ix_mensaje_saliente_estado_creado_en");
    }
}
