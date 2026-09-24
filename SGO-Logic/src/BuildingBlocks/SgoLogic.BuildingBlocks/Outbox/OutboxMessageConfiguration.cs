using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>
/// Cada servicio aplica esta configuracion a SU PROPIO DbContext con
/// modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration()) dentro de su
/// OnModelCreating. El nombre de tabla y columnas es fijo a proposito: es lo que
/// permite que OutboxPublisher use el mismo FromSqlRaw contra cualquier contexto.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.EventId).HasColumnName("event_id").IsRequired().HasMaxLength(64);
        builder.Property(m => m.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(200);
        builder.Property(m => m.Payload).HasColumnName("payload").IsRequired();
        builder.Property(m => m.TransactionId).HasColumnName("transaction_id").IsRequired().HasMaxLength(64);
        builder.Property(m => m.Exchange).HasColumnName("exchange").IsRequired().HasMaxLength(200);
        builder.Property(m => m.RoutingKey).HasColumnName("routing_key").IsRequired().HasMaxLength(255);
        builder.Property(m => m.AggregateType).HasColumnName("aggregate_type").IsRequired().HasMaxLength(200);
        builder.Property(m => m.AggregateId).HasColumnName("aggregate_id").IsRequired().HasMaxLength(200);
        builder.Property(m => m.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
        builder.Property(m => m.FechaPublicacion).HasColumnName("fecha_publicacion");
        builder.Property(m => m.IntentosFallidos).HasColumnName("intentos_fallidos").IsRequired();
        builder.Property(m => m.ProximoIntentoEn).HasColumnName("proximo_intento_en");

        builder.Property(m => m.Estado)
            .HasColumnName("estado")
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(m => m.EventId).IsUnique();
        builder.HasIndex(m => new { m.Estado, m.ProximoIntentoEn });
    }
}