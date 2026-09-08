using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class LineaOrdenConfiguration : IEntityTypeConfiguration<LineaOrden>
{
    public void Configure(EntityTypeBuilder<LineaOrden> builder)
    {
        builder.ToTable("linea_orden");

        builder.HasKey(linea => linea.Id);

        builder.Property(linea => linea.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<string>("OrdenId")
            .HasColumnName("orden_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex("OrdenId")
            .HasDatabaseName("ix_linea_orden_orden_id");

        builder.Property(linea => linea.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(linea => linea.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(linea => linea.Cantidad)
            .HasColumnName("cantidad")
            .IsRequired();

        builder.Property(linea => linea.PrecioUnitario)
            .HasColumnName("precio_unitario")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Ignore(linea => linea.Subtotal);
    }
}
