using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class PerfilCrediticioConfiguration : IEntityTypeConfiguration<PerfilCrediticio>
{
    public void Configure(EntityTypeBuilder<PerfilCrediticio> builder)
    {
        builder.ToTable("perfil_crediticio");

        builder.HasKey(perfil => perfil.ClienteId);

        builder.Property(perfil => perfil.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(HistorialCliente.LargoMaximoClienteId)
            .ValueGeneratedNever();

        builder.Property(perfil => perfil.Puntaje).HasColumnName("puntaje").IsRequired();

        builder.Property(perfil => perfil.Nivel)
            .HasColumnName("nivel")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(perfil => perfil.Elegible).HasColumnName("elegible").IsRequired();

        builder.Property(perfil => perfil.CodigoMotivo)
            .HasColumnName("codigo_motivo")
            .HasMaxLength(40);

        builder.Property(perfil => perfil.LimiteAprobado)
            .HasColumnName("limite_aprobado")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(perfil => perfil.PuntosPuntualidad).HasColumnName("puntos_puntualidad").HasPrecision(5, 2).IsRequired();
        builder.Property(perfil => perfil.PuntosMonto).HasColumnName("puntos_monto").HasPrecision(5, 2).IsRequired();
        builder.Property(perfil => perfil.PuntosAntiguedad).HasColumnName("puntos_antiguedad").HasPrecision(5, 2).IsRequired();
        builder.Property(perfil => perfil.PuntosFrecuencia).HasColumnName("puntos_frecuencia").HasPrecision(5, 2).IsRequired();
        builder.Property(perfil => perfil.PuntosDevoluciones).HasColumnName("puntos_devoluciones").HasPrecision(5, 2).IsRequired();
        builder.Property(perfil => perfil.PuntosIncidentes).HasColumnName("puntos_incidentes").HasPrecision(5, 2).IsRequired();

        builder.Property(perfil => perfil.VersionPolitica)
            .HasColumnName("version_politica")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(perfil => perfil.FechaCorteHistorial)
            .HasColumnName("fecha_corte_historial")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(perfil => perfil.CalculadoEn)
            .HasColumnName("calculado_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(perfil => perfil.Secuencia).HasColumnName("secuencia").IsRequired();

        // Bloqueo optimista: cada recalculo incrementa la version.
        builder.Property(perfil => perfil.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();
    }
}
