namespace SgoLogic.Contracts.Eventos.Credito;

/// <summary>
/// El perfil crediticio de un cliente cambio (puntaje, nivel, elegibilidad, motivo o
/// limite). Lo publica Credito via outbox solo cuando hay un cambio real; un
/// recalculo con el mismo resultado no emite nada. aggregateId = email del cliente.
/// </summary>
public static class PerfilCrediticioActualizado
{
    public const string EventType = "PerfilCrediticioActualizado";
    public const string RoutingKey = "credito.perfil.actualizado";
    public const string AggregateType = "PerfilCrediticio";
}

/// <param name="Nivel">EXCELENTE, BUENO, REGULAR o NO_CALIFICA.</param>
/// <param name="CodigoMotivo">Motivo del descarte cuando Elegible es false; null si es elegible.</param>
public sealed record PerfilCrediticioActualizadoData(
    string ClienteId,
    int Puntaje,
    string Nivel,
    bool Elegible,
    string? CodigoMotivo,
    decimal LimiteAprobado,
    string Moneda,
    string VersionPolitica,
    DateTime FechaCorteHistorial,
    DateTime CalculadoEn);
