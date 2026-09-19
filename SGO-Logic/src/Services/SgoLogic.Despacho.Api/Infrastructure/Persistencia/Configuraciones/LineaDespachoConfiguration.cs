using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Despacho.Api.Domain;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class LineaDespachoConfiguration : IEntityTypeConfiguration<LineaDespacho>
{
    public void Configure(EntityTypeBuilder<LineaDespacho> builder)
    {
        builder.ToTable("linea_despacho");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(l => l.DespachoId).HasColumnName("despacho_id").HasMaxLength(30).IsRequired();
        builder.Property(l => l.Sku).HasColumnName("sku").HasMaxLength(40).IsRequired();
        builder.Property(l => l.Descripcion).HasColumnName("descripcion").HasMaxLength(200).IsRequired();
        builder.Property(l => l.Cantidad).HasColumnName("cantidad").IsRequired();

        builder.HasIndex(l => l.DespachoId).HasDatabaseName("ix_linea_despacho_despacho_id");
    }
}
