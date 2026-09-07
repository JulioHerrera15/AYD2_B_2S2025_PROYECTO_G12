using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SgoLogic.BuildingBlocks.Serialization;

/// <summary>
/// Serializa decimales siempre como cadena con dos posiciones decimales
/// (ej. "1250.00"), nunca como numero JSON, para evitar perdida de precision
/// entre lenguajes al transportar montos.
/// </summary>
public sealed class DecimalComoTextoConverter : JsonConverter<decimal>
{
    private const string Formato = "0.00";

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var texto = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : reader.GetDecimal().ToString(CultureInfo.InvariantCulture);

        return decimal.Parse(texto!, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Formato, CultureInfo.InvariantCulture));
    }
}