using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class EnlaceReintentoConfiguration : IEntityTypeConfiguration<EnlaceReintento>
{
    public void Configure(EntityTypeBuilder<EnlaceReintento> builder)
    {
        builder.ToTable("enlace_reintento");

        builder.HasKey(enlace => enlace.Id);
        builder.Property(enlace => enlace.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(enlace => enlace.Token)
            .HasColumnName("token")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(enlace => enlace.Token)
            .IsUnique()
            .HasDatabaseName("ix_enlace_reintento_token");

        builder.Property(enlace => enlace.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(enlace => enlace.OrdenId)
            .HasDatabaseName("ix_enlace_reintento_orden_id");

        builder.Property(enlace => enlace.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(enlace => enlace.Usado)
            .HasColumnName("usado")
            .IsRequired();

        builder.Property(enlace => enlace.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();
    }
}
