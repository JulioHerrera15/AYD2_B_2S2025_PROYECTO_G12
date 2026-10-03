using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class CuentaPorCobrarConfiguration : IEntityTypeConfiguration<CuentaPorCobrar>
{
    public void Configure(EntityTypeBuilder<CuentaPorCobrar> builder)
    {
        builder.ToTable("cuenta_por_cobrar", tabla =>
        {
            tabla.HasCheckConstraint("ck_cuenta_por_cobrar_monto_positivo", "monto_original > 0");
            tabla.HasCheckConstraint("ck_cuenta_por_cobrar_saldo_no_negativo", "saldo_pendiente >= 0");
        });

        builder.HasKey(cuenta => cuenta.Id);

        builder.Property(cuenta => cuenta.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cuenta => cuenta.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(CuentaPorCobrar.LargoMaximoOrdenId)
            .IsRequired();

        // Una cuenta por orden: un segundo cargo para la misma orden choca en la base.
        builder.HasIndex(cuenta => cuenta.OrdenId)
            .IsUnique()
            .HasDatabaseName("ux_cuenta_por_cobrar_orden_id");

        builder.Property(cuenta => cuenta.NumeroOrden)
            .HasColumnName("numero_orden")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(cuenta => cuenta.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .IsRequired();

        builder.HasIndex(cuenta => cuenta.ClienteId)
            .HasDatabaseName("ix_cuenta_por_cobrar_cliente_id");

        builder.Property(cuenta => cuenta.MontoOriginal)
            .HasColumnName("monto_original")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(cuenta => cuenta.SaldoPendiente)
            .HasColumnName("saldo_pendiente")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(cuenta => cuenta.Moneda)
            .HasColumnName("moneda")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(cuenta => cuenta.EmitidaEn)
            .HasColumnName("emitida_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(cuenta => cuenta.VenceEn)
            .HasColumnName("vence_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(cuenta => cuenta.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(cuenta => cuenta.AnuladaEn)
            .HasColumnName("anulada_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(cuenta => cuenta.MotivoAnulacion)
            .HasColumnName("motivo_anulacion")
            .HasMaxLength(CuentaPorCobrar.LargoMaximoMotivo);

        builder.Property(cuenta => cuenta.PagadaEn)
            .HasColumnName("pagada_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(cuenta => cuenta.PagoPuntual)
            .HasColumnName("pago_puntual");

        builder.Property(cuenta => cuenta.RecordatorioEnviadoEn)
            .HasColumnName("recordatorio_enviado_en")
            .HasColumnType("timestamp with time zone");

        // El worker de cobranza barre por estado y fecha de vencimiento.
        builder.HasIndex(cuenta => new { cuenta.Estado, cuenta.VenceEn })
            .HasDatabaseName("ix_cuenta_por_cobrar_estado_vence_en");

        builder.Property(cuenta => cuenta.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(cuenta => cuenta.EsPagable);
        builder.Ignore(cuenta => cuenta.AnuladaPorDevolucion);
    }
}
