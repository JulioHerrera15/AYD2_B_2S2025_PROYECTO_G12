using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Auth.Api.Domain;

namespace SgoLogic.Auth.Api.Infrastructure.Configuraciones;

public sealed class CodigoVerificacionConfiguration : IEntityTypeConfiguration<CodigoVerificacion>
{
    public void Configure(EntityTypeBuilder<CodigoVerificacion> builder)
    {
        builder.ToTable("codigos_verificacion");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.UsuarioId)
            .HasColumnName("usuario_id");

        builder.Property(c => c.HashCodigo)
            .HasColumnName("hash_codigo")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamptz");

        builder.Property(c => c.Usado)
            .HasColumnName("usado")
            .HasDefaultValue(false);

        builder.Property(c => c.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz");

        builder.HasOne(c => c.Usuario)
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.UsuarioId)
            .HasDatabaseName("ix_codigos_verificacion_usuario_id");
    }
}
