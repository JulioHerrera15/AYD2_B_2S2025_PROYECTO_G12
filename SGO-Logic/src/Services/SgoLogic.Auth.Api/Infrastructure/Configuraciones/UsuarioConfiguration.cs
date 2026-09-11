using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Auth.Api.Domain;

namespace SgoLogic.Auth.Api.Infrastructure.Configuraciones;

public sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id");

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_usuarios_email");

        builder.Property(u => u.HashContrasena)
            .HasColumnName("hash_contrasena")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.NombreCompleto)
            .HasColumnName("nombre_completo")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(u => u.EmailVerificado)
            .HasColumnName("email_verificado")
            .HasDefaultValue(false);

        builder.Property(u => u.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz");

        builder.Property(u => u.ActualizadoEn)
            .HasColumnName("actualizado_en")
            .HasColumnType("timestamptz");
    }
}
