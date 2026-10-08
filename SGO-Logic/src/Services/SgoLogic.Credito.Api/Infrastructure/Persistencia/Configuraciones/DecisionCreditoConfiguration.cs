using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class DecisionCreditoConfiguration : IEntityTypeConfiguration<DecisionCredito>
{
    public void Configure(EntityTypeBuilder<DecisionCredito> builder)
    {
        builder.ToTable("decision_credito");

        builder.HasKey(decision => decision.Id);

        builder.Property(decision => decision.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(decision => decision.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(CuentaPorCobrar.LargoMaximoOrdenId)
            .IsRequired();

        // Idempotencia de la evaluacion: una sola decision por orden.
        builder.HasIndex(decision => decision.OrdenId)
            .IsUnique()
            .HasDatabaseName("ux_decision_credito_orden_id");

        builder.Property(decision => decision.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .IsRequired();

        builder.Property(decision => decision.Monto)
            .HasColumnName("monto")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(decision => decision.Resultado)
            .HasColumnName("resultado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(decision => decision.CodigoMotivo)
            .HasColumnName("codigo_motivo")
            .HasMaxLength(40);

        builder.Property(decision => decision.Puntaje).HasColumnName("puntaje").IsRequired();

        builder.Property(decision => decision.Nivel)
            .HasColumnName("nivel")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(decision => decision.LimiteAprobado)
            .HasColumnName("limite_aprobado")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(decision => decision.DisponibleAntes)
            .HasColumnName("disponible_antes")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(decision => decision.VersionPolitica)
            .HasColumnName("version_politica")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(decision => decision.DecididoEn)
            .HasColumnName("decidido_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();
    }
}
