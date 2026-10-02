namespace SgoLogic.Web.Services;

/// <summary>
/// Helper de formateo de fechas y horas para el portal web.
/// Centraliza la conversion de marcas de tiempo UTC generadas por los microservicios
/// a la zona horaria oficial de Guatemala (America/Guatemala, UTC-6) de forma segura,
/// resolviendo discrepancias cuando el frontend se ejecuta en contenedores Docker (UTC).
/// </summary>
public static class FechaHelper
{
    private static readonly TimeZoneInfo ZonaHorariaGuatemala = ObtenerZonaHorariaGuatemala();

    private static TimeZoneInfo ObtenerZonaHorariaGuatemala()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Guatemala_CST",
                    TimeSpan.FromHours(-6),
                    "Hora de Guatemala",
                    "Hora de Guatemala");
            }
        }
    }

    /// <summary>
    /// Convierte una fecha y hora UTC a la hora local de Guatemala (UTC-6).
    /// </summary>
    public static DateTime AFechaLocal(DateTime utcDateTime)
    {
        if (utcDateTime == default || utcDateTime == DateTime.MinValue || utcDateTime == DateTime.MaxValue)
            return utcDateTime;

        var utc = utcDateTime.Kind switch
        {
            DateTimeKind.Utc => utcDateTime,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            DateTimeKind.Local => utcDateTime.ToUniversalTime(),
            _ => utcDateTime
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, ZonaHorariaGuatemala);
    }

    /// <summary>
    /// Convierte una fecha y hora UTC opcional a la hora local de Guatemala.
    /// </summary>
    public static DateTime? AFechaLocal(DateTime? utcDateTime)
    {
        if (!utcDateTime.HasValue || utcDateTime.Value == default || utcDateTime.Value == DateTime.MinValue || utcDateTime.Value == DateTime.MaxValue)
            return null;

        return AFechaLocal(utcDateTime.Value);
    }

    /// <summary>
    /// Formatea una fecha y hora en formato legible con zona horaria local de Guatemala.
    /// </summary>
    public static string Formatear(DateTime? utcDateTime, string formato = "dd/MM/yyyy HH:mm")
    {
        var local = AFechaLocal(utcDateTime);
        return local.HasValue ? local.Value.ToString(formato) : "N/D";
    }

    /// <summary>
    /// Formatea una fecha y hora no nula en formato legible con zona horaria local de Guatemala.
    /// </summary>
    public static string Formatear(DateTime utcDateTime, string formato = "dd/MM/yyyy HH:mm")
    {
        if (utcDateTime == default || utcDateTime == DateTime.MinValue || utcDateTime == DateTime.MaxValue)
            return "N/D";

        return AFechaLocal(utcDateTime).ToString(formato);
    }

    /// <summary>
    /// Formatea unicamente la fecha (dd/MM/yyyy).
    /// </summary>
    public static string FormatearSoloFecha(DateTime? utcDateTime) =>
        Formatear(utcDateTime, "dd/MM/yyyy");

    /// <summary>
    /// Formatea unicamente la fecha (dd/MM/yyyy).
    /// </summary>
    public static string FormatearSoloFecha(DateTime utcDateTime) =>
        Formatear(utcDateTime, "dd/MM/yyyy");

    /// <summary>
    /// Formatea unicamente la hora (HH:mm).
    /// </summary>
    public static string FormatearSoloHora(DateTime? utcDateTime) =>
        Formatear(utcDateTime, "HH:mm");

    /// <summary>
    /// Formatea unicamente la hora (HH:mm).
    /// </summary>
    public static string FormatearSoloHora(DateTime utcDateTime) =>
        Formatear(utcDateTime, "HH:mm");
}
