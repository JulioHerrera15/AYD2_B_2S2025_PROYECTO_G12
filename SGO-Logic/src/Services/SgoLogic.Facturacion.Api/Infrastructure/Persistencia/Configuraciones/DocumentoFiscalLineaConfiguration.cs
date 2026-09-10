using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Facturacion.Api.Domain;

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class DocumentoFiscalLineaConfiguration : IEntityTypeConfiguration<DocumentoFiscalLinea>
{
    public void Configure(EntityTypeBuilder<DocumentoFiscalLinea> builder)
    {
        builder.ToTable("documento_fiscal_linea");

        builder.HasKey(linea => linea.Id);
        builder.Property(linea => linea.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property<Guid>("DocumentoFiscalId").HasColumnName("documento_fiscal_id").IsRequired();

        builder.HasIndex("DocumentoFiscalId", nameof(DocumentoFiscalLinea.NumeroLinea))
            .IsUnique()
            .HasDatabaseName("ix_documento_fiscal_linea_documento_numero");

        builder.Property(linea => linea.NumeroLinea)
            .HasColumnName("numero_linea")
            .IsRequired();

        builder.Property(linea => linea.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(linea => linea.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(linea => linea.Cantidad)
            .HasColumnName("cantidad")
            .IsRequired();

        builder.Property(linea => linea.PrecioUnitario)
            .HasColumnName("precio_unitario")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(linea => linea.Impuesto)
            .HasColumnName("impuesto")
            .HasPrecision(18, 2)
            .IsRequired();
    }
}
