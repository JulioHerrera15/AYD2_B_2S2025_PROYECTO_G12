using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class PagoConfiguration : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> builder)
    {
        builder.ToTable("pago");

        builder.HasKey(pago => pago.Id);
        builder.Property(pago => pago.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(pago => pago.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(pago => pago.OrdenId)
            .IsUnique()
            .HasDatabaseName("ix_pago_orden_id");

        builder.Property(pago => pago.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(pago => pago.Canal)
            .HasColumnName("canal")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(pago => pago.Moneda)
            .HasColumnName("moneda")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(pago => pago.Monto)
            .HasColumnName("monto")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(pago => pago.MedioPago)
            .HasColumnName("medio_pago")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(pago => pago.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(pago => pago.Secuencia)
            .HasColumnName("secuencia")
            .IsRequired();

        builder.Property(pago => pago.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(pago => pago.ActualizadoEn)
            .HasColumnName("actualizado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.HasMany(pago => pago.Intentos)
            .WithOne()
            .HasForeignKey("PagoId")
            .HasConstraintName("fk_intento_pago_pago")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Pago.Intentos))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(pago => pago.SiguienteNumeroIntento);
    }
}
