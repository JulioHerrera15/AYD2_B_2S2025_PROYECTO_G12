using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class ReservaCarritoConfiguration : IEntityTypeConfiguration<ReservaCarrito>
{
    public const string IndiceRetencionActiva = "ix_reserva_carrito_activa";

    public void Configure(EntityTypeBuilder<ReservaCarrito> builder)
    {
        builder.ToTable("reserva_carrito");

        builder.HasKey(retencion => retencion.Id);

        builder.Property(retencion => retencion.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(retencion => retencion.CarritoId)
            .HasColumnName("carrito_id")
            .HasMaxLength(ReservaCarrito.LargoMaximoCarritoId)
            .IsRequired();

        builder.Property(retencion => retencion.Sku)
            .HasColumnName("sku")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(retencion => retencion.UbicacionId)
            .HasColumnName("ubicacion_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(retencion => retencion.TipoUbicacion)
            .HasColumnName("tipo_ubicacion")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(retencion => retencion.UbicacionPreferidaId)
            .HasColumnName("ubicacion_preferida_id")
            .HasMaxLength(64);

        builder.Property(retencion => retencion.Cantidad)
            .HasColumnName("cantidad")
            .IsRequired();

        builder.Property(retencion => retencion.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(retencion => retencion.CreadaEn)
            .HasColumnName("creada_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        builder.Property(retencion => retencion.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>()
            .IsRequired();

        // Mismo bloqueo optimista que la existencia (ADR-005): dos pestanas del mismo
        // carrito no pueden pisarse la retencion en silencio.
        builder.Property(retencion => retencion.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        // Una sola retencion ACTIVA por carrito y SKU. Si dos peticiones concurrentes
        // intentan crearla, la segunda choca aqui y la unidad de trabajo lo traduce a
        // conflicto de concurrencia.
        builder.HasIndex(retencion => new { retencion.CarritoId, retencion.Sku })
            .IsUnique()
            .HasFilter("estado = 'ACTIVA'")
            .HasDatabaseName(IndiceRetencionActiva);

        // Barrido de expiracion: WHERE estado = 'ACTIVA' AND expira_en <= ahora.
        builder.HasIndex(retencion => new { retencion.Estado, retencion.ExpiraEn })
            .HasDatabaseName("ix_reserva_carrito_estado_expira_en");

        builder.Ignore(retencion => retencion.EstaActiva);
        builder.Ignore(retencion => retencion.EstaVigente);
        builder.Ignore(retencion => retencion.EnOtraSucursal);
    }
}
