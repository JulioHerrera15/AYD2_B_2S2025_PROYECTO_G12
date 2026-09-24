using System.Text.Json;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.BuildingBlocks.Tests.Serialization;

public class SgoJsonSerializerOptionsTests
{
    private sealed record Muestra(string NombreProducto, decimal Monto, DateTime Fecha, Estado Estado);

    private enum Estado
    {
        ACTIVO,
        INACTIVO
    }

    [Fact]
    public void Serializa_propiedades_en_camelCase()
    {
        var muestra = new Muestra("Silla", 10m, DateTime.UtcNow, Estado.ACTIVO);

        var json = JsonSerializer.Serialize(muestra, SgoJsonSerializerOptions.Default);

        Assert.Contains("\"nombreProducto\"", json);
        Assert.DoesNotContain("\"NombreProducto\"", json);
    }

    [Theory]
    [InlineData(1250.00, "\"1250.00\"")]
    [InlineData(10, "\"10.00\"")]
    [InlineData(0.5, "\"0.50\"")]
    public void Serializa_decimales_como_cadena_con_dos_posiciones(decimal monto, string fragmentoEsperado)
    {
        var muestra = new Muestra("x", monto, DateTime.UtcNow, Estado.ACTIVO);

        var json = JsonSerializer.Serialize(muestra, SgoJsonSerializerOptions.Default);

        Assert.Contains($"\"monto\":{fragmentoEsperado}", json);
    }

    [Fact]
    public void Serializa_fechas_en_UTC_ISO8601_con_milisegundos_y_Z()
    {
        var fecha = new DateTime(2026, 9, 7, 15, 4, 5, 123, DateTimeKind.Utc);
        var muestra = new Muestra("x", 1m, fecha, Estado.ACTIVO);

        var json = JsonSerializer.Serialize(muestra, SgoJsonSerializerOptions.Default);

        Assert.Contains("\"fecha\":\"2026-09-07T15:04:05.123Z\"", json);
    }

    [Fact]
    public void Serializa_enumeraciones_como_cadena()
    {
        var muestra = new Muestra("x", 1m, DateTime.UtcNow, Estado.INACTIVO);

        var json = JsonSerializer.Serialize(muestra, SgoJsonSerializerOptions.Default);

        Assert.Contains("\"estado\":\"INACTIVO\"", json);
    }

    [Fact]
    public void Deserializa_lo_que_serializo_sin_perder_datos()
    {
        var original = new Muestra("Silla", 1250.00m, DateTime.UtcNow, Estado.ACTIVO);

        var json = JsonSerializer.Serialize(original, SgoJsonSerializerOptions.Default);
        var reconstruido = JsonSerializer.Deserialize<Muestra>(json, SgoJsonSerializerOptions.Default);

        Assert.NotNull(reconstruido);
        Assert.Equal(original.NombreProducto, reconstruido!.NombreProducto);
        Assert.Equal(original.Monto, reconstruido.Monto);
        Assert.Equal(original.Estado, reconstruido.Estado);
    }
}