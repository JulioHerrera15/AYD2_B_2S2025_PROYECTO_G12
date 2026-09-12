using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class DevolucionConfiguration : IEntityTypeConfiguration<Devolucion>
{
    public void Configure(EntityTypeBuilder<Devolucion> builder)
    {
        builder.ToTable("devolucion");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(d => d.OrdenOriginalId)
            .HasColumnName("orden_original_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(d => d.OrdenOriginalId)
            .HasDatabaseName("ix_devolucion_orden_original_id");

        builder.Property(d => d.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.Tipo)
            .HasColumnName("tipo")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.EsParcial)
            .HasColumnName("es_parcial")
            .IsRequired();

        builder.Property(d => d.UbicacionReingresoId)
            .HasColumnName("ubicacion_reingreso_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.MontoReembolso)
            .HasColumnName("monto_reembolso")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(d => d.PagoOriginalId)
            .HasColumnName("pago_original_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(d => d.DocumentoFiscalOriginalId)
            .HasColumnName("documento_fiscal_original_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(d => d.Moneda)
            .HasColumnName("moneda")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(d => d.RegistradaPor)
            .HasColumnName("registrada_por")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(d => d.RegistradaEn)
            .HasColumnName("registrada_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.HasMany(d => d.Lineas)
            .WithOne()
            .HasForeignKey("DevolucionId")
            .HasConstraintName("fk_linea_devolucion_devolucion")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Devolucion.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
