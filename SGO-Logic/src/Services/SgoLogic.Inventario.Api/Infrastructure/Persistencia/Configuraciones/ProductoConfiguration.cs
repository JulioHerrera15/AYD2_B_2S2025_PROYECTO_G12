using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class ProductoConfiguration : IEntityTypeConfiguration<Producto>
{
    public void Configure(EntityTypeBuilder<Producto> builder)
    {
        builder.ToTable("producto");

        builder.HasKey(producto => producto.Sku);

        // Mismo largo que existencia.sku: es el mismo identificador.
        builder.Property(producto => producto.Sku)
            .HasColumnName("sku")
            .HasMaxLength(Producto.LargoMaximoSku)
            .ValueGeneratedNever();

        builder.Property(producto => producto.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(Producto.LargoMaximoNombre)
            .IsRequired();

        builder.Property(producto => producto.Categoria)
            .HasColumnName("categoria")
            .HasMaxLength(Producto.LargoMaximoCategoria)
            .IsRequired();

        builder.Property(producto => producto.Precio)
            .HasColumnName("precio")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(producto => producto.Icono)
            .HasColumnName("icono")
            .HasMaxLength(Producto.LargoMaximoIcono)
            .IsRequired();

        builder.Property(producto => producto.Activo)
            .HasColumnName("activo")
            .IsRequired();

        builder.Property(producto => producto.Descripcion)
            .HasColumnName("descripcion")
            .HasMaxLength(Producto.LargoMaximoDescripcion);

        builder.Property(producto => producto.Etiqueta)
            .HasColumnName("etiqueta")
            .HasMaxLength(Producto.LargoMaximoEtiqueta);

        builder.HasData(ProductosSemilla.Todos);
    }
}
