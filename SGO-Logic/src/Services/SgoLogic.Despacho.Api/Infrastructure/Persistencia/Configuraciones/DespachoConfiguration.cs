using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia.Configuraciones;

public sealed class DespachoConfiguration : IEntityTypeConfiguration<Domain.Despacho>
{
    public void Configure(EntityTypeBuilder<Domain.Despacho> builder)
    {
        builder.ToTable("despacho");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id").HasMaxLength(30).ValueGeneratedNever();

        builder.Property(d => d.OrdenId).HasColumnName("orden_id").HasMaxLength(30).IsRequired();
        builder.Property(d => d.ReservaId).HasColumnName("reserva_id").HasMaxLength(40).IsRequired();

        builder.Property(d => d.ClienteId).HasColumnName("cliente_id").HasMaxLength(254);

        builder.Property(d => d.MetodoEntrega)
            .HasColumnName("metodo_entrega").HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(d => d.UbicacionOrigenId).HasColumnName("ubicacion_origen_id").HasMaxLength(64);

        builder.Property(d => d.TipoUbicacionOrigen)
            .HasColumnName("tipo_ubicacion_origen").HasConversion<string>().HasMaxLength(30);

        builder.Property(d => d.UbicacionRecogidaId).HasColumnName("ubicacion_recogida_id").HasMaxLength(64);
        builder.Property(d => d.DireccionEntrega).HasColumnName("direccion_entrega").HasMaxLength(400);

        builder.Property(d => d.Estado)
            .HasColumnName("estado").HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(d => d.GuiaId).HasColumnName("guia_id").HasMaxLength(40);
        builder.Property(d => d.TrackingUrl).HasColumnName("tracking_url").HasMaxLength(400);
        builder.Property(d => d.Transportista).HasColumnName("transportista").HasMaxLength(80);
        builder.Property(d => d.DetalleEstado).HasColumnName("detalle_estado").HasMaxLength(400);

        builder.Property(d => d.Secuencia).HasColumnName("secuencia").IsRequired();

        builder.Property(d => d.CreadoEn)
            .HasColumnName("creado_en").HasColumnType("timestamp with time zone")
            .HasConversion<ConvertidorFechaUtc>().IsRequired();

        builder.Property(d => d.EntregadoEn)
            .HasColumnName("entregado_en").HasColumnType("timestamp with time zone");

        // Bloqueo optimista (ADR-005), igual que en Inventario.
        builder.Property(d => d.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        // Un despacho por orden: es lo que hace idempotente el consumo del evento.
        builder.HasIndex(d => d.OrdenId).IsUnique().HasDatabaseName("ix_despacho_orden_id");

        // Cubre la cola de preparacion del operador (HU-019).
        builder.HasIndex(d => new { d.UbicacionOrigenId, d.Estado })
            .HasDatabaseName("ix_despacho_ubicacion_estado");

        builder.Metadata
            .FindNavigation(nameof(Domain.Despacho.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(d => d.Lineas)
            .WithOne()
            .HasForeignKey(l => l.DespachoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(d => d.RequiereGuia);
    }
}
