namespace SgoLogic.BuildingBlocks.Correlation;

/// <summary>
/// Contexto de correlacion ambiental para la solicitud o el mensaje en curso.
/// Usa AsyncLocal para fluir automaticamente a traves de codigo asincrono sin
/// pasar el transactionId explicitamente por cada capa.
/// </summary>
public static class EventContext
{
    private static readonly AsyncLocal<string?> TransactionIdActual = new();
    private static readonly AsyncLocal<string?> CausationIdActual = new();

    public static string? TransactionId
    {
        get => TransactionIdActual.Value;
        set => TransactionIdActual.Value = value;
    }

    public static string? CausationId
    {
        get => CausationIdActual.Value;
        set => CausationIdActual.Value = value;
    }
}