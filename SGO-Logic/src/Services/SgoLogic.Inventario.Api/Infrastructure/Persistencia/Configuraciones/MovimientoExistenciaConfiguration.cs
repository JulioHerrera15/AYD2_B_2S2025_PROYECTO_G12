using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class MovimientoExistenciaConfiguration : IEntityTypeConfiguration<MovimientoExistencia>
{
    public void Configure(EntityTypeBuilder<MovimientoExistencia> builder)
    {
        builder.ToTable("movimiento_existencia");

        builder.HasKey(movimiento => movimiento.Id);

        builder.Property(movimiento => movimiento.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(movimiento => movimiento.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(movimiento => movimiento.UbicacionId)
            .HasColumnName("ubicacion_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(movimiento => movimiento.Tipo)
            .HasColumnName("tipo")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(movimiento => movimiento.Cantidad)
            .HasColumnName("cantidad")
            .IsRequired();

        builder.Property(movimiento => movimiento.CantidadFisicaResultante)
            .HasColumnName("cantidad_fisica_resultante")
            .IsRequired();

        builder.Property(movimiento => movimiento.Motivo)
            .HasColumnName("motivo")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(movimiento => movimiento.RegistradoEn)
            .HasColumnName("registrado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        // No hay FK a existencia: el ledger es append-only y debe sobrevivir aunque
        // la existencia se reorganice. El indice cubre la consulta del historial,
        // que siempre filtra por SKU y opcionalmente por ubicacion, ordenando por
        // fecha descendente.
        builder.HasIndex(movimiento => new
            {
                movimiento.Sku,
                movimiento.UbicacionId,
                movimiento.RegistradoEn
            })
            .HasDatabaseName("ix_movimiento_sku_ubicacion_registrado_en");
    }
}
