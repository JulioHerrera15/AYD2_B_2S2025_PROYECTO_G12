using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class SucursalConfiguration : IEntityTypeConfiguration<Sucursal>
{
    public void Configure(EntityTypeBuilder<Sucursal> builder)
    {
        builder.ToTable("sucursal");

        builder.HasKey(sucursal => sucursal.Id);

        // Mismo largo que existencia.ubicacion_id: es el mismo identificador.
        builder.Property(sucursal => sucursal.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(sucursal => sucursal.Nombre)
            .HasColumnName("nombre")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(sucursal => sucursal.TipoUbicacion)
            .HasColumnName("tipo_ubicacion")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(sucursal => sucursal.Departamento)
            .HasColumnName("departamento")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(sucursal => sucursal.Municipio)
            .HasColumnName("municipio")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(sucursal => sucursal.Latitud)
            .HasColumnName("latitud");

        builder.Property(sucursal => sucursal.Longitud)
            .HasColumnName("longitud");

        builder.Property(sucursal => sucursal.Activa)
            .HasColumnName("activa")
            .IsRequired();

        builder.Ignore(sucursal => sucursal.TieneCoordenadas);

        builder.HasData(SucursalesSemilla.Todas);
    }
}
