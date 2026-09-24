using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class DevolucionLineaConfiguration : IEntityTypeConfiguration<DevolucionLinea>
{
    public void Configure(EntityTypeBuilder<DevolucionLinea> builder)
    {
        builder.ToTable("linea_devolucion");

        builder.HasKey(linea => linea.Id);

        builder.Property(linea => linea.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<string>("DevolucionId")
            .HasColumnName("devolucion_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex("DevolucionId")
            .HasDatabaseName("ix_linea_devolucion_devolucion_id");

        builder.Property(linea => linea.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(linea => linea.Cantidad)
            .HasColumnName("cantidad")
            .IsRequired();

        builder.Property(linea => linea.Motivo)
            .HasColumnName("motivo")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(linea => linea.SkuReemplazo)
            .HasColumnName("sku_reemplazo")
            .HasMaxLength(40);

        builder.Property(linea => linea.PrecioUnitario)
            .HasColumnName("precio_unitario")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Ignore(linea => linea.Subtotal);
    }
}
