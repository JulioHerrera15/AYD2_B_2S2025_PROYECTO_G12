using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Tests.Domain;

public sealed class FechasCreditoTests
{
    [Fact]
    public void Normalizar_trunca_a_microsegundos_exactos()
    {
        var fecha = DatosPrueba.Ahora.AddTicks(7);

        var normalizada = FechasCredito.Normalizar(fecha);

        Assert.Equal(DatosPrueba.Ahora, normalizada);
        Assert.Equal(0, normalizada.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(DateTimeKind.Utc, normalizada.Kind);
    }

    [Fact]
    public void Normalizar_no_cambia_una_fecha_que_ya_esta_en_microsegundos()
    {
        var fecha = DatosPrueba.Ahora.AddTicks(3 * TimeSpan.TicksPerMicrosecond);

        Assert.Equal(fecha, FechasCredito.Normalizar(fecha));
    }

    [Fact]
    public void Normalizar_marca_como_UTC_una_fecha_sin_especificar()
    {
        var fecha = DateTime.SpecifyKind(DatosPrueba.Ahora, DateTimeKind.Unspecified).AddTicks(5);

        var normalizada = FechasCredito.Normalizar(fecha);

        Assert.Equal(DateTimeKind.Utc, normalizada.Kind);
        Assert.Equal(DatosPrueba.Ahora, normalizada);
    }
}
