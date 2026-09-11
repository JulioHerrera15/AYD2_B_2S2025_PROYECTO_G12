using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class ReservaConfiguration : IEntityTypeConfiguration<Reserva>
{
    public void Configure(EntityTypeBuilder<Reserva> builder)
    {
        builder.ToTable("reserva");

        builder.HasKey(reserva => reserva.Id);

        builder.Property(reserva => reserva.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(reserva => reserva.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(reserva => reserva.OrdenId)
            .IsUnique()
            .HasDatabaseName("ix_reserva_orden_id");

        builder.Property(reserva => reserva.UbicacionId)
            .HasColumnName("ubicacion_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(reserva => reserva.TipoUbicacion)
            .HasColumnName("tipo_ubicacion")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(reserva => reserva.TtlSegundos)
            .HasColumnName("ttl_segundos")
            .IsRequired();

        builder.Property(reserva => reserva.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reserva => reserva.Secuencia)
            .HasColumnName("secuencia")
            .IsRequired();

        builder.Property(reserva => reserva.CreadaEn)
            .HasColumnName("creada_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(reserva => reserva.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.HasIndex(reserva => new { reserva.Estado, reserva.ExpiraEn })
            .HasDatabaseName("ix_reserva_estado_expira_en");

        builder.HasMany(reserva => reserva.Lineas)
            .WithOne()
            .HasForeignKey("ReservaId")
            .HasConstraintName("fk_linea_reserva_reserva")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Reserva.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(reserva => reserva.EstaActiva);
    }
}
