using System.Text.Json;
using SgoLogic.Contracts;

namespace SgoLogic.Auditoria.Tests;

/// <summary>Construye envelopes IntegrationEvent&lt;JsonElement&gt; de prueba sin depender de ningun tipo concreto de Contracts.</summary>
public static class EventosDePrueba
{
    public static IntegrationEvent<JsonElement> Crear(
        string? transactionId = null,
        string? aggregateId = null,
        int sequence = 1,
        string eventType = "Prueba.Ocurrio",
        string source = "prueba",
        object? data = null)
    {
        var dataJson = JsonSerializer.SerializeToElement(data ?? new { mensaje = "dato de prueba" });

        return new IntegrationEvent<JsonElement>
        {
            EventType = eventType,
            Sequence = sequence,
            TransactionId = transactionId ?? Guid.NewGuid().ToString(),
            AggregateType = "AgregadoPrueba",
            AggregateId = aggregateId ?? Guid.NewGuid().ToString(),
            Source = source,
            Data = dataJson
        };
    }
}