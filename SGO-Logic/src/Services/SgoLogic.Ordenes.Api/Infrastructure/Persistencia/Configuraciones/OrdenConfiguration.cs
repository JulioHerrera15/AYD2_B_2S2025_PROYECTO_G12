using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class OrdenConfiguration : IEntityTypeConfiguration<Orden>
{
    public void Configure(EntityTypeBuilder<Orden> builder)
    {
        builder.ToTable("orden");

        builder.HasKey(orden => orden.Id);

        builder.Property(orden => orden.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(orden => orden.NumeroOrden)
            .HasColumnName("numero_orden")
            .HasMaxLength(40)
            .IsRequired();

        builder.HasIndex(orden => orden.NumeroOrden)
            .IsUnique()
            .HasDatabaseName("ix_orden_numero_orden");

        builder.Property(orden => orden.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(orden => orden.Canal)
            .HasColumnName("canal")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(orden => orden.MetodoEntrega)
            .HasColumnName("metodo_entrega")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(orden => orden.UbicacionSolicitadaId)
            .HasColumnName("ubicacion_solicitada_id")
            .HasMaxLength(64);

        builder.Property(orden => orden.Moneda)
            .HasColumnName("moneda")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(orden => orden.Impuestos)
            .HasColumnName("impuestos")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(orden => orden.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        // El dominio no expone una propiedad llamada Version. Su rol de contador
        // entero que se incrementa en cada transicion lo cumple Orden.Secuencia,
        // que aqui se mapea a la columna version y se marca como token de
        // concurrencia para el bloqueo optimista (ADR del DDA).
        builder.Property(orden => orden.Secuencia)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(orden => orden.CreadaEn)
            .HasColumnName("creada_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.HasMany(orden => orden.Lineas)
            .WithOne()
            .HasForeignKey("OrdenId")
            .HasConstraintName("fk_linea_orden_orden")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Orden.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(orden => orden.Transiciones)
            .WithOne()
            .HasForeignKey(transicion => transicion.OrdenId)
            .HasConstraintName("fk_transicion_orden_orden")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Orden.Transiciones))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(orden => orden.Subtotal);
        builder.Ignore(orden => orden.Total);
        builder.Ignore(orden => orden.EsTerminal);
    }
}
