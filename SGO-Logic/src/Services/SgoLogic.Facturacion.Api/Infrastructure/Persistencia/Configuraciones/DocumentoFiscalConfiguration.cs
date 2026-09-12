using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Facturacion.Api.Domain;

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class DocumentoFiscalConfiguration : IEntityTypeConfiguration<DocumentoFiscal>
{
    public void Configure(EntityTypeBuilder<DocumentoFiscal> builder)
    {
        builder.ToTable("documento_fiscal");

        builder.HasKey(documento => documento.Id);
        builder.Property(documento => documento.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(documento => documento.OrdenId)
            .HasColumnName("orden_id")
            .HasMaxLength(64)
            .IsRequired();

        // No es unico: una orden tiene su FACTURA y, si hay devoluciones, una
        // NOTA_CREDITO por cada una (todas comparten OrdenId).
        builder.HasIndex(documento => documento.OrdenId)
            .HasDatabaseName("ix_documento_fiscal_orden_id");

        builder.Property(documento => documento.TransactionId)
            .HasColumnName("transaction_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(documento => documento.TipoDocumento)
            .HasColumnName("tipo_documento")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(documento => documento.DocumentoOrigenId)
            .HasColumnName("documento_origen_id")
            .HasMaxLength(64);

        builder.Property(documento => documento.CuiReceptor)
            .HasColumnName("cui_receptor")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(documento => documento.NombreReceptor)
            .HasColumnName("nombre_receptor")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(documento => documento.DireccionReceptor)
            .HasColumnName("direccion_receptor")
            .HasMaxLength(400)
            .IsRequired();

        builder.Property(documento => documento.PaisReceptor)
            .HasColumnName("pais_receptor")
            .HasConversion<string>()
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(documento => documento.Moneda)
            .HasColumnName("moneda")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(documento => documento.Subtotal)
            .HasColumnName("subtotal")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(documento => documento.Impuestos)
            .HasColumnName("impuestos")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(documento => documento.Total)
            .HasColumnName("total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(documento => documento.Serie)
            .HasColumnName("serie")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(documento => documento.Correlativo)
            .HasColumnName("correlativo")
            .IsRequired();

        builder.Property(documento => documento.NumeroDocumento)
            .HasColumnName("numero_documento")
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(documento => new { documento.Serie, documento.Correlativo })
            .IsUnique()
            .HasDatabaseName("ix_documento_fiscal_serie_correlativo");

        // Se guarda como text: el contenido es lo que importa y evita depender del
        // cast text->xml de PostgreSQL en cada INSERT.
        builder.Property(documento => documento.Xml)
            .HasColumnName("xml_generado")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(documento => documento.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(documento => documento.Intentos)
            .HasColumnName("intentos")
            .IsRequired();

        // El dominio siempre deja las fechas nullables en UTC (ToUniversalTime).
        builder.Property(documento => documento.ProximoIntentoEn)
            .HasColumnName("proximo_intento_en")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(documento => new { documento.Estado, documento.ProximoIntentoEn })
            .HasDatabaseName("ix_documento_fiscal_estado_proximo_intento_en");

        builder.Property(documento => documento.Folio)
            .HasColumnName("folio")
            .HasMaxLength(64);

        builder.Property(documento => documento.NumeroAutorizacion)
            .HasColumnName("numero_autorizacion")
            .HasMaxLength(64);

        builder.Property(documento => documento.UltimoCodigo)
            .HasColumnName("ultimo_codigo")
            .HasMaxLength(40);

        builder.Property(documento => documento.UltimoMotivo)
            .HasColumnName("ultimo_motivo")
            .HasMaxLength(500);

        builder.Property(documento => documento.Secuencia)
            .HasColumnName("secuencia")
            .IsRequired();

        builder.Property(documento => documento.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(documento => documento.ActualizadoEn)
            .HasColumnName("actualizado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(documento => documento.CertificadoEn)
            .HasColumnName("certificado_en")
            .HasColumnType("timestamp with time zone");

        builder.HasMany(documento => documento.Lineas)
            .WithOne()
            .HasForeignKey("DocumentoFiscalId")
            .HasConstraintName("fk_documento_fiscal_linea_documento")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(DocumentoFiscal.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
