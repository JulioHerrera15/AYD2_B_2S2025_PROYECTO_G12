using System.Text.Json;
using System.Text.Json.Serialization;

namespace SgoLogic.BuildingBlocks.Serialization;

/// <summary>
/// Punto unico de definicion de las reglas de serializacion compartidas por todos
/// los servicios: camelCase, decimales y montos como cadena, fechas UTC ISO 8601
/// con milisegundos y Z, enumeraciones como cadena.
/// </summary>
public static class SgoJsonSerializerOptions
{
    public static readonly JsonSerializerOptions Default = Crear();

    public static JsonSerializerOptions Crear()
    {
        var opciones = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        opciones.Converters.Add(new JsonStringEnumConverter());
        opciones.Converters.Add(new DecimalComoTextoConverter());
        opciones.Converters.Add(new FechaUtcConverter());

        return opciones;
    }

    /// <summary>
    /// Copia las reglas compartidas dentro de otra instancia de JsonSerializerOptions
    /// (por ejemplo, las que usan MVC o el JSON de minimal APIs).
    /// </summary>
    public static void AplicarA(JsonSerializerOptions destino)
    {
        destino.PropertyNamingPolicy = Default.PropertyNamingPolicy;
        destino.PropertyNameCaseInsensitive = Default.PropertyNameCaseInsensitive;

        foreach (var convertidor in Default.Converters)
        {
            destino.Converters.Add(convertidor);
        }
    }
}