using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class TransicionOrdenConfiguration : IEntityTypeConfiguration<TransicionOrden>
{
    public void Configure(EntityTypeBuilder<TransicionOrden> builder)
    {
        builder.ToTable("transicion_orden");

        builder.HasKey(transicion => transicion.Id);

        builder.Property(transicion => transicion.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(transicion => transicion.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(transicion => transicion.Secuencia)
            .HasColumnName("secuencia")
            .IsRequired();

        builder.HasIndex(transicion => new { transicion.OrdenId, transicion.Secuencia })
            .IsUnique()
            .HasDatabaseName("ix_transicion_orden_orden_id_secuencia");

        builder.Property(transicion => transicion.EstadoAnterior)
            .HasColumnName("estado_anterior")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(transicion => transicion.EstadoNuevo)
            .HasColumnName("estado_nuevo")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(transicion => transicion.Motivo)
            .HasColumnName("motivo")
            .HasMaxLength(500);

        builder.Property(transicion => transicion.PasoSaga)
            .HasColumnName("paso_saga")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(transicion => transicion.EsCompensacion)
            .HasColumnName("es_compensacion")
            .IsRequired();

        builder.Property(transicion => transicion.CambiadoEn)
            .HasColumnName("cambiado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();
    }
}
