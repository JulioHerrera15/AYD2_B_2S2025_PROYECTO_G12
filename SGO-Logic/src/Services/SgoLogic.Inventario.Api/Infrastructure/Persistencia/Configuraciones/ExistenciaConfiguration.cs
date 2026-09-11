using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class ExistenciaConfiguration : IEntityTypeConfiguration<Existencia>
{
    public void Configure(EntityTypeBuilder<Existencia> builder)
    {
        builder.ToTable("existencia");

        builder.HasKey(existencia => existencia.Id);

        builder.Property(existencia => existencia.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(existencia => existencia.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(existencia => existencia.UbicacionId)
            .HasColumnName("ubicacion_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(existencia => existencia.TipoUbicacion)
            .HasColumnName("tipo_ubicacion")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(existencia => existencia.CantidadFisica)
            .HasColumnName("cantidad_fisica")
            .IsRequired();

        builder.Property(existencia => existencia.CantidadReservada)
            .HasColumnName("cantidad_reservada")
            .IsRequired();

        // Bloqueo optimista de ADR-005: la columna version es el token de
        // concurrencia. EF Core la incluye en el WHERE de todo UPDATE y lanza
        // DbUpdateConcurrencyException si no coincide.
        builder.Property(existencia => existencia.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(existencia => new { existencia.Sku, existencia.UbicacionId })
            .IsUnique()
            .HasDatabaseName("ix_existencia_sku_ubicacion");

        builder.Ignore(existencia => existencia.Disponible);
    }
}
