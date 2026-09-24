using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class IntentoPagoConfiguration : IEntityTypeConfiguration<IntentoPago>
{
    public void Configure(EntityTypeBuilder<IntentoPago> builder)
    {
        builder.ToTable("intento_pago");

        builder.HasKey(intento => intento.Id);
        builder.Property(intento => intento.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property<Guid>("PagoId").HasColumnName("pago_id").IsRequired();

        builder.HasIndex("PagoId", nameof(IntentoPago.IntentoNumero))
            .IsUnique()
            .HasDatabaseName("ix_intento_pago_pago_id_intento_numero");

        builder.Property(intento => intento.IntentoNumero)
            .HasColumnName("intento_numero")
            .IsRequired();

        builder.Property(intento => intento.MedioPago)
            .HasColumnName("medio_pago")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(intento => intento.TokenMedioPago)
            .HasColumnName("token_medio_pago")
            .HasMaxLength(200);

        builder.Property(intento => intento.UltimosCuatro)
            .HasColumnName("ultimos_cuatro")
            .HasMaxLength(4);

        builder.Property(intento => intento.Marca)
            .HasColumnName("marca")
            .HasMaxLength(20);

        builder.Property(intento => intento.Monto)
            .HasColumnName("monto")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(intento => intento.Moneda)
            .HasColumnName("moneda")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(intento => intento.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(intento => intento.CodigoRespuesta)
            .HasColumnName("codigo_respuesta")
            .HasMaxLength(40);

        builder.Property(intento => intento.MensajeRespuesta)
            .HasColumnName("mensaje_respuesta")
            .HasMaxLength(500);

        builder.Property(intento => intento.LatenciaMs)
            .HasColumnName("latencia_ms");

        builder.Property(intento => intento.AutorizacionId)
            .HasColumnName("autorizacion_id")
            .HasMaxLength(64);

        builder.Property(intento => intento.IniciadoEn)
            .HasColumnName("iniciado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        // El dominio ya deja FinalizadoEn en UTC.
        builder.Property(intento => intento.FinalizadoEn)
            .HasColumnName("finalizado_en")
            .HasColumnType("timestamp with time zone");
    }
}
