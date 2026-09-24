using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SgoLogic.Auditoria.Worker.Domain;

namespace SgoLogic.Auditoria.Worker.Infrastructure;

/// <summary>
/// Mapeo de la tabla de solo insercion. Los indices reflejan exactamente los dos
/// patrones de consulta que expone la API (por transactionId y por aggregateId)
/// mas la unicidad de eventId como ancla de idempotencia. No hay indice GIN sobre
/// payload: ninguna consulta filtra por contenido del jsonb, solo se recupera
/// completo una vez localizada la fila por transactionId/aggregateId, asi que un
/// GIN aca solo costaria escritura sin acelerar nada.
/// </summary>
public sealed class EventoAuditoriaConfiguration : IEntityTypeConfiguration<EventoAuditoria>
{
    public void Configure(EntityTypeBuilder<EventoAuditoria> builder)
    {
        builder.ToTable("eventos_auditoria");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.EventId).HasColumnName("event_id").IsRequired().HasMaxLength(64);
        builder.Property(e => e.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(200);
        builder.Property(e => e.EventVersion).HasColumnName("event_version").IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(e => e.TransactionId).HasColumnName("transaction_id").IsRequired().HasMaxLength(64);
        builder.Property(e => e.CausationId).HasColumnName("causation_id").HasMaxLength(64);
        builder.Property(e => e.AggregateType).HasColumnName("aggregate_type").IsRequired().HasMaxLength(200);
        builder.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired().HasMaxLength(200);
        builder.Property(e => e.Source).HasColumnName("source").IsRequired().HasMaxLength(100);
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.FechaRecepcion).HasColumnName("fecha_recepcion").IsRequired();

        builder.HasIndex(e => e.EventId).IsUnique();
        builder.HasIndex(e => e.TransactionId);
        builder.HasIndex(e => e.AggregateId);
    }
}