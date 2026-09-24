using System.Text.Json;

namespace SgoLogic.Auditoria.Worker.Application;

/// <summary>Proyeccion de lectura de una fila de eventos_auditoria. Data se reexpone como JSON real (no como cadena escapada).</summary>
public sealed record EventoAuditoriaDto(
    string EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAt,
    int Sequence,
    string TransactionId,
    string? CausationId,
    string AggregateType,
    string AggregateId,
    string Source,
    JsonElement Data,
    DateTime FechaRecepcion);

/// <summary>Resultado paginado de solo lectura.</summary>
public sealed record PaginaResultado<T>(IReadOnlyList<T> Items, int Pagina, int Tamano, int Total);
