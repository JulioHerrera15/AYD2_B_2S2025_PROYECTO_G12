using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Sample.Api.Domain;

namespace SgoLogic.Sample.Api.Infrastructure.Configuraciones;

public sealed class ItemEjemploConfiguration : IEntityTypeConfiguration<ItemEjemplo>
{
    public void Configure(EntityTypeBuilder<ItemEjemplo> builder)
    {
        builder.ToTable("items_ejemplo");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");

        builder.Property(item => item.Nombre)
            .HasColumnName("nombre")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(item => item.CreadoEn)
            .HasColumnName("creado_en")
            .IsRequired();
    }
}