using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class ReembolsoConfiguration : IEntityTypeConfiguration<Reembolso>
{
    public void Configure(EntityTypeBuilder<Reembolso> builder)
    {
        builder.ToTable("reembolso", tabla =>
            tabla.HasCheckConstraint("ck_reembolso_monto_positivo", "monto > 0"));

        builder.HasKey(reembolso => reembolso.Id);
        builder.Property(reembolso => reembolso.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(reembolso => reembolso.Concepto)
            .HasColumnName("concepto")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reembolso => reembolso.ReferenciaId)
            .HasColumnName("referencia_id")
            .HasMaxLength(64)
            .IsRequired();

        // Idempotencia de la creacion: un reembolso por orden cancelada y uno por
        // solicitud de Credito.
        builder.HasIndex(reembolso => new { reembolso.Concepto, reembolso.ReferenciaId })
            .IsUnique()
            .HasDatabaseName("ux_reembolso_concepto_referencia");

        builder.Property(reembolso => reembolso.PagoId).HasColumnName("pago_id").IsRequired();
        builder.HasIndex(reembolso => reembolso.PagoId).HasDatabaseName("ix_reembolso_pago_id");

        builder.HasOne<Pago>()
            .WithMany()
            .HasForeignKey(reembolso => reembolso.PagoId)
            .HasConstraintName("fk_reembolso_pago")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(reembolso => reembolso.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(reembolso => reembolso.AbonoId).HasColumnName("abono_id").HasMaxLength(64);

        builder.Property(reembolso => reembolso.Monto).HasColumnName("monto").HasPrecision(18, 2).IsRequired();

        builder.Property(reembolso => reembolso.Moneda)
            .HasColumnName("moneda")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(reembolso => reembolso.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(reembolso => reembolso.Intentos).HasColumnName("intentos").IsRequired();

        builder.Property(reembolso => reembolso.ProximoIntentoEn)
            .HasColumnName("proximo_intento_en")
            .HasColumnType("timestamp with time zone");

        // El worker barre los PENDIENTE vencidos.
        builder.HasIndex(reembolso => new { reembolso.Estado, reembolso.ProximoIntentoEn })
            .HasDatabaseName("ix_reembolso_estado_proximo_intento");

        builder.Property(reembolso => reembolso.AutorizacionId)
            .HasColumnName("autorizacion_id")
            .HasMaxLength(Reembolso.LargoMaximoCodigo);

        builder.Property(reembolso => reembolso.CodigoRespuesta)
            .HasColumnName("codigo_respuesta")
            .HasMaxLength(Reembolso.LargoMaximoCodigo);

        builder.Property(reembolso => reembolso.MotivoFalla)
            .HasColumnName("motivo_falla")
            .HasMaxLength(Reembolso.LargoMaximoMotivo);

        builder.Property(reembolso => reembolso.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(reembolso => reembolso.ResueltoEn)
            .HasColumnName("resuelto_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(reembolso => reembolso.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Ignore(reembolso => reembolso.EsFinal);
    }
}
