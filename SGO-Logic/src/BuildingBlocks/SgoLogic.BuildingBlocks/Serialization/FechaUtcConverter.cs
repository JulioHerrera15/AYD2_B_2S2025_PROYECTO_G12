using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SgoLogic.BuildingBlocks.Serialization;

/// <summary>
/// Serializa DateTime siempre en UTC, formato ISO 8601 con milisegundos y sufijo Z
/// (ej. "2026-09-07T15:04:05.123Z").
/// </summary>
public sealed class FechaUtcConverter : JsonConverter<DateTime>
{
    private const string Formato = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var texto = reader.GetString();
        return DateTime.Parse(
            texto!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString(Formato, CultureInfo.InvariantCulture));
    }
}