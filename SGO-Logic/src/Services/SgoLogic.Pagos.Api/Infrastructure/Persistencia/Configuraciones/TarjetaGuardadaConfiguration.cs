using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class TarjetaGuardadaConfiguration : IEntityTypeConfiguration<TarjetaGuardada>
{
    public void Configure(EntityTypeBuilder<TarjetaGuardada> builder)
    {
        builder.ToTable("tarjeta_guardada");

        builder.HasKey(tarjeta => tarjeta.Id);

        builder.Property(tarjeta => tarjeta.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(tarjeta => tarjeta.ClienteId)
            .HasColumnName("cliente_id")
            .HasMaxLength(TarjetaGuardada.LargoMaximoClienteId)
            .IsRequired();

        builder.Property(tarjeta => tarjeta.ReferenciaPayKlik)
            .HasColumnName("referencia_payklik")
            .HasMaxLength(TarjetaGuardada.LargoMaximoReferencia)
            .IsRequired();

        builder.Property(tarjeta => tarjeta.Marca)
            .HasColumnName("marca")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Solo los ultimos cuatro: la columna no admite un numero completo.
        builder.Property(tarjeta => tarjeta.Ultimos4)
            .HasColumnName("ultimos4")
            .HasMaxLength(4)
            .IsFixedLength()
            .IsRequired();

        builder.Property(tarjeta => tarjeta.MesExp)
            .HasColumnName("mes_exp")
            .IsRequired();

        builder.Property(tarjeta => tarjeta.AnioExp)
            .HasColumnName("anio_exp")
            .IsRequired();

        builder.Property(tarjeta => tarjeta.Titular)
            .HasColumnName("titular")
            .HasMaxLength(TarjetaGuardada.LargoMaximoTitular)
            .IsRequired();

        builder.Property(tarjeta => tarjeta.Activa)
            .HasColumnName("activa")
            .IsRequired();

        builder.Property(tarjeta => tarjeta.CreadaEn)
            .HasColumnName("creada_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(tarjeta => tarjeta.UltimoUsoEn)
            .HasColumnName("ultimo_uso_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>();

        // Una sola tarjeta activa por cliente con la misma marca, terminacion y
        // vencimiento: guardarla otra vez actualiza la referencia en lugar de duplicar.
        builder.HasIndex(tarjeta => new { tarjeta.ClienteId, tarjeta.Marca, tarjeta.Ultimos4, tarjeta.MesExp, tarjeta.AnioExp })
            .IsUnique()
            .HasFilter("activa")
            .HasDatabaseName("ix_tarjeta_guardada_cliente_tarjeta_activa");

        builder.Ignore(tarjeta => tarjeta.Enmascarado);
    }
}
