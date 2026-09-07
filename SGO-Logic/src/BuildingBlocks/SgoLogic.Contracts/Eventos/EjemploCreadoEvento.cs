namespace SgoLogic.Contracts.Eventos;

/// <summary>
/// Datos de ejemplo para ilustrar el uso del envelope. No representa un evento real
/// de ningun contexto delimitado.
/// </summary>
public sealed record EjemploCreadoData(string Nombre);

public static class EjemploCreadoEvento
{
    public const string TipoEvento = "ejemplo.creado";
    public const string TipoAgregado = "Ejemplo";

    public static IntegrationEvent<EjemploCreadoData> Crear(
        string transactionId,
        string aggregateId,
        string source,
        EjemploCreadoData data,
        string? causationId = null)
    {
        return new IntegrationEvent<EjemploCreadoData>
        {
            EventType = TipoEvento,
            TransactionId = transactionId,
            CausationId = causationId,
            AggregateType = TipoAgregado,
            AggregateId = aggregateId,
            Source = source,
            Data = data
        };
    }
}