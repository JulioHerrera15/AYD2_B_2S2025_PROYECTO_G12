namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Normalizacion de fechas que entran a Credito y se persisten. PostgreSQL guarda
/// timestamptz con precision de microsegundos y Npgsql trunca los ticks sobrantes
/// de .NET (100 ns); si se compara el valor en memoria contra el releido, una
/// misma fecha parece mas nueva. Normalizar en la entrada hace que ambas
/// comparaciones den lo mismo.
/// </summary>
public static class FechasCredito
{
    /// <summary>La fecha en UTC y truncada a microsegundos exactos, igual que la guarda PostgreSQL.</summary>
    public static DateTime Normalizar(DateTime fecha)
    {
        var utc = fecha.Kind switch
        {
            DateTimeKind.Utc => fecha,
            DateTimeKind.Local => fecha.ToUniversalTime(),
            _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
        };

        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }
}
