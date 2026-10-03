using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class MovimientoCuentaConfiguration : IEntityTypeConfiguration<MovimientoCuenta>
{
    public void Configure(EntityTypeBuilder<MovimientoCuenta> builder)
    {
        builder.ToTable("movimiento_cuenta", tabla =>
            tabla.HasCheckConstraint("ck_movimiento_cuenta_monto_positivo", "monto > 0"));

        builder.HasKey(movimiento => movimiento.Id);
        builder.Property(movimiento => movimiento.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(movimiento => movimiento.CuentaId).HasColumnName("cuenta_id").IsRequired();
        builder.HasIndex(movimiento => movimiento.CuentaId).HasDatabaseName("ix_movimiento_cuenta_cuenta_id");

        builder.Property(movimiento => movimiento.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .IsRequired();

        builder.HasIndex(movimiento => movimiento.ClienteId).HasDatabaseName("ix_movimiento_cuenta_cliente_id");

        builder.Property(movimiento => movimiento.Tipo)
            .HasColumnName("tipo")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(movimiento => movimiento.Referencia)
            .HasColumnName("referencia")
            .HasMaxLength(MovimientoCuenta.LargoMaximoReferencia)
            .IsRequired();

        // Idempotencia: una devolucion (o una confirmacion de reembolso) se aplica una vez.
        builder.HasIndex(movimiento => new { movimiento.Tipo, movimiento.Referencia })
            .IsUnique()
            .HasDatabaseName("ux_movimiento_cuenta_tipo_referencia");

        builder.Property(movimiento => movimiento.Monto).HasColumnName("monto").HasPrecision(12, 2).IsRequired();
        builder.Property(movimiento => movimiento.MontoDescontadoDeuda)
            .HasColumnName("monto_descontado_deuda")
            .HasPrecision(12, 2)
            .IsRequired();
        builder.Property(movimiento => movimiento.MontoAReembolsar)
            .HasColumnName("monto_a_reembolsar")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(movimiento => movimiento.AbonoId).HasColumnName("abono_id");

        builder.Property(movimiento => movimiento.Fecha)
            .HasColumnName("fecha")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();
    }
}
