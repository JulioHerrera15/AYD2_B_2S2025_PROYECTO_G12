using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class LineaReservaConfiguration : IEntityTypeConfiguration<LineaReserva>
{
    public void Configure(EntityTypeBuilder<LineaReserva> builder)
    {
        builder.ToTable("linea_reserva");

        builder.HasKey(linea => linea.Id);

        builder.Property(linea => linea.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<string>("ReservaId")
            .HasColumnName("reserva_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex("ReservaId")
            .HasDatabaseName("ix_linea_reserva_reserva_id");

        builder.Property(linea => linea.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(linea => linea.Cantidad)
            .HasColumnName("cantidad")
            .IsRequired();
    }
}
