using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.Tests.Contracts;

public class IntegrationEventTests
{
    private sealed record DatosEjemplo(string Valor);

    [Fact]
    public void Genera_eventId_como_uuid_valido_por_defecto()
    {
        var evento = new IntegrationEvent<DatosEjemplo>
        {
            EventType = "ejemplo.creado",
            TransactionId = Guid.NewGuid().ToString(),
            AggregateType = "Ejemplo",
            AggregateId = Guid.NewGuid().ToString(),
            Source = "ejemplo",
            Data = new DatosEjemplo("x"),
            Sequence = 1
        };

        Assert.True(Guid.TryParse(evento.EventId, out _));
    }

    [Fact]
    public void EventVersion_por_defecto_es_1()
    {
        var evento = new IntegrationEvent<DatosEjemplo>
        {
            EventType = "ejemplo.creado",
            TransactionId = Guid.NewGuid().ToString(),
            AggregateType = "Ejemplo",
            AggregateId = Guid.NewGuid().ToString(),
            Source = "ejemplo",
            Data = new DatosEjemplo("x"),
            Sequence = 1
        };

        Assert.Equal(1, evento.EventVersion);
    }

    [Fact]
    public void OccurredAt_por_defecto_es_UTC_y_reciente()
    {
        var antes = DateTime.UtcNow;

        var evento = new IntegrationEvent<DatosEjemplo>
        {
            EventType = "ejemplo.creado",
            TransactionId = Guid.NewGuid().ToString(),
            AggregateType = "Ejemplo",
            AggregateId = Guid.NewGuid().ToString(),
            Source = "ejemplo",
            Data = new DatosEjemplo("x"),
            Sequence = 1
        };

        var despues = DateTime.UtcNow;

        Assert.Equal(DateTimeKind.Utc, evento.OccurredAt.Kind);
        Assert.InRange(evento.OccurredAt, antes.AddSeconds(-1), despues.AddSeconds(1));
    }

    [Fact]
    public void CausationId_es_null_cuando_no_se_indica()
    {
        var evento = new IntegrationEvent<DatosEjemplo>
        {
            EventType = "ejemplo.creado",
            TransactionId = Guid.NewGuid().ToString(),
            AggregateType = "Ejemplo",
            AggregateId = Guid.NewGuid().ToString(),
            Source = "ejemplo",
            Data = new DatosEjemplo("x"),
            Sequence = 1
        };

        Assert.Null(evento.CausationId);
    }

    [Fact]
    public void TransactionId_y_aggregateId_no_se_confunden()
    {
        var transactionId = Guid.NewGuid().ToString();
        var aggregateId = Guid.NewGuid().ToString();

        var evento = new IntegrationEvent<DatosEjemplo>
        {
            EventType = "ejemplo.creado",
            TransactionId = transactionId,
            AggregateType = "Ejemplo",
            AggregateId = aggregateId,
            Source = "ejemplo",
            Data = new DatosEjemplo("x"),
            Sequence = 1
        };

        Assert.Equal(transactionId, evento.TransactionId);
        Assert.Equal(aggregateId, evento.AggregateId);
        Assert.NotEqual(evento.TransactionId, evento.AggregateId);
    }
}
