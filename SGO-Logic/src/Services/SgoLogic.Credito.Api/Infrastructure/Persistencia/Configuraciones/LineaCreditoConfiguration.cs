using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class LineaCreditoConfiguration : IEntityTypeConfiguration<LineaCredito>
{
    public void Configure(EntityTypeBuilder<LineaCredito> builder)
    {
        builder.ToTable("linea_credito", tabla =>
        {
            tabla.HasCheckConstraint("ck_linea_credito_limite_no_negativo", "limite_aprobado >= 0");
            tabla.HasCheckConstraint("ck_linea_credito_saldo_no_negativo", "saldo_utilizado >= 0");
        });

        builder.HasKey(linea => linea.ClienteId);

        builder.Property(linea => linea.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .ValueGeneratedNever();

        builder.Property(linea => linea.LimiteAprobado)
            .HasColumnName("limite_aprobado")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(linea => linea.SaldoUtilizado)
            .HasColumnName("saldo_utilizado")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(linea => linea.Moneda)
            .HasColumnName("moneda")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(linea => linea.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(linea => linea.ActualizadoEn)
            .HasColumnName("actualizado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        // Bloqueo optimista: los cargos y abonos del siguiente bloque compiten por la misma fila.
        builder.Property(linea => linea.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(linea => linea.Disponible);
    }
}
