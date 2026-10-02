using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class HistorialClienteConfiguration : IEntityTypeConfiguration<HistorialCliente>
{
    public void Configure(EntityTypeBuilder<HistorialCliente> builder)
    {
        builder.ToTable("historial_cliente");

        builder.HasKey(historial => historial.ClienteId);

        builder.Property(historial => historial.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .ValueGeneratedNever();

        builder.Property(historial => historial.CodigoClienteErp)
            .HasColumnName("codigo_cliente_erp")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(historial => historial.ClienteDesde)
            .HasColumnName("cliente_desde")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(historial => historial.ComprasUltimos12Meses)
            .HasColumnName("compras_ultimos_12_meses")
            .IsRequired();

        builder.Property(historial => historial.MontoCompradoUltimos12Meses)
            .HasColumnName("monto_comprado_ultimos_12_meses")
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(historial => historial.Moneda)
            .HasColumnName("moneda")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(historial => historial.PagosPuntuales).HasColumnName("pagos_puntuales").IsRequired();
        builder.Property(historial => historial.PagosTardios).HasColumnName("pagos_tardios").IsRequired();
        builder.Property(historial => historial.MaxDiasAtraso).HasColumnName("max_dias_atraso").IsRequired();
        builder.Property(historial => historial.Devoluciones).HasColumnName("devoluciones").IsRequired();
        builder.Property(historial => historial.IncidentesGraves).HasColumnName("incidentes_graves").IsRequired();
        builder.Property(historial => historial.OtrosIncidentes).HasColumnName("otros_incidentes").IsRequired();

        builder.Property(historial => historial.FechaCorte)
            .HasColumnName("fecha_corte")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(historial => historial.LoteOrigen)
            .HasColumnName("lote_origen")
            .HasMaxLength(100);

        builder.Property(historial => historial.RecibidoEn)
            .HasColumnName("recibido_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Ignore(historial => historial.TasaDevolucion);
    }
}
