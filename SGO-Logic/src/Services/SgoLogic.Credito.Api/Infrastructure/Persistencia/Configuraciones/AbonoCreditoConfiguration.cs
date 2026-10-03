using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class AbonoCreditoConfiguration : IEntityTypeConfiguration<AbonoCredito>
{
    public void Configure(EntityTypeBuilder<AbonoCredito> builder)
    {
        builder.ToTable("abono_credito", tabla =>
            tabla.HasCheckConstraint("ck_abono_credito_monto_positivo", "monto > 0"));

        builder.HasKey(abono => abono.Id);
        builder.Property(abono => abono.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(abono => abono.Referencia)
            .HasColumnName("referencia")
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(abono => abono.Referencia)
            .IsUnique()
            .HasDatabaseName("ux_abono_credito_referencia");

        builder.Property(abono => abono.CuentaId).HasColumnName("cuenta_id").IsRequired();

        // Un solo abono en curso por cuenta: dos solicitudes simultaneas chocan aqui.
        builder.HasIndex(abono => abono.CuentaId)
            .IsUnique()
            .HasFilter("estado IN ('SOLICITADO', 'PENDIENTE')")
            .HasDatabaseName("ux_abono_credito_cuenta_en_curso");

        // Historial y anulacion buscan los abonos de una cuenta por estado.
        builder.HasIndex(abono => new { abono.CuentaId, abono.Estado })
            .HasDatabaseName("ix_abono_credito_cuenta_estado");

        builder.Property(abono => abono.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .IsRequired();

        builder.HasIndex(abono => abono.ClienteId).HasDatabaseName("ix_abono_credito_cliente_id");

        builder.Property(abono => abono.Monto).HasColumnName("monto").HasPrecision(12, 2).IsRequired();
        builder.Property(abono => abono.Moneda).HasColumnName("moneda").HasMaxLength(3).IsRequired();

        builder.Property(abono => abono.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired();

        builder.Property(abono => abono.IntentoNumero).HasColumnName("intento_numero").IsRequired();
        builder.Property(abono => abono.CodigoRespuesta).HasColumnName("codigo_respuesta").HasMaxLength(AbonoCredito.LargoMaximoCodigo);
        builder.Property(abono => abono.AutorizacionId).HasColumnName("autorizacion_id").HasMaxLength(AbonoCredito.LargoMaximoCodigo);
        builder.Property(abono => abono.UltimosCuatro).HasColumnName("ultimos_cuatro").HasMaxLength(4);
        builder.Property(abono => abono.Marca).HasColumnName("marca").HasMaxLength(AbonoCredito.LargoMaximoCodigo);

        builder.Property(abono => abono.SolicitadoEn)
            .HasColumnName("solicitado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(abono => abono.ResueltoEn)
            .HasColumnName("resuelto_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(abono => abono.MontoReembolsado).HasColumnName("monto_reembolsado").HasPrecision(12, 2).IsRequired();
        builder.Property(abono => abono.MontoReembolsoSolicitado)
            .HasColumnName("monto_reembolso_solicitado")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(abono => abono.ReembolsoSolicitadoEn)
            .HasColumnName("reembolso_solicitado_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(abono => abono.ReembolsoFallidoEn)
            .HasColumnName("reembolso_fallido_en")
            .HasColumnType("timestamp with time zone");

        builder.Property(abono => abono.MotivoFallaReembolso)
            .HasColumnName("motivo_falla_reembolso")
            .HasMaxLength(AbonoCredito.LargoMaximoMotivoFalla);

        // El worker busca los REEMBOLSO_PENDIENTE que aun no tienen solicitud.
        builder.HasIndex(abono => abono.Estado)
            .HasFilter("reembolso_solicitado_en IS NULL")
            .HasDatabaseName("ix_abono_credito_reembolso_sin_solicitud");

        builder.Property(abono => abono.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.Ignore(abono => abono.EnCurso);
        builder.Ignore(abono => abono.EsFinal);
        builder.Ignore(abono => abono.PuedeReintentar);
        builder.Ignore(abono => abono.PendientePorSolicitarReembolso);
    }
}
