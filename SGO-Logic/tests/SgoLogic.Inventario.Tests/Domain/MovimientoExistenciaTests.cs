using SgoLogic.Inventario.Api.Domain;
using Xunit;

namespace SgoLogic.Inventario.Tests.Domain;

/// <summary>
/// HU-016: el asiento del ledger deriva su sentido del signo del delta y se niega
/// a existir cuando no hubo movimiento real.
/// </summary>
public sealed class MovimientoExistenciaTests
{
    private const string Sku = "SKU-LB-00871";
    private const string Ubicacion = "CD-01";

    [Fact]
    public void Un_delta_positivo_es_una_entrada()
    {
        var movimiento = new MovimientoExistencia(
            Sku, Ubicacion, 4, 14, MotivoMovimiento.AJUSTE_MANUAL, DateTime.UtcNow);

        Assert.Equal(TipoMovimiento.ENTRADA, movimiento.Tipo);
        Assert.Equal(4, movimiento.Cantidad);
    }

    [Fact]
    public void Un_delta_negativo_es_una_salida_y_la_cantidad_queda_positiva()
    {
        var movimiento = new MovimientoExistencia(
            Sku, Ubicacion, -7, 3, MotivoMovimiento.AJUSTE_MANUAL, DateTime.UtcNow);

        Assert.Equal(TipoMovimiento.SALIDA, movimiento.Tipo);

        // El signo vive en Tipo, no en Cantidad: asi el historial se suma sin cuidar signos.
        Assert.Equal(7, movimiento.Cantidad);
    }

    [Fact]
    public void Un_delta_de_cero_no_es_un_movimiento()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovimientoExistencia(
            Sku, Ubicacion, 0, 10, MotivoMovimiento.AJUSTE_MANUAL, DateTime.UtcNow));
    }

    [Fact]
    public void La_fecha_se_normaliza_a_utc()
    {
        var local = new DateTime(2026, 9, 13, 8, 30, 0, DateTimeKind.Local);

        var movimiento = new MovimientoExistencia(
            Sku, Ubicacion, 1, 1, MotivoMovimiento.CARGA_INICIAL, local);

        Assert.Equal(DateTimeKind.Utc, movimiento.RegistradoEn.Kind);
        Assert.Equal(local.ToUniversalTime(), movimiento.RegistradoEn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void El_sku_es_obligatorio(string sku)
    {
        Assert.Throws<ArgumentException>(() => new MovimientoExistencia(
            sku, Ubicacion, 1, 1, MotivoMovimiento.CARGA_INICIAL, DateTime.UtcNow));
    }

    [Fact]
    public void La_cantidad_resultante_no_puede_ser_negativa()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovimientoExistencia(
            Sku, Ubicacion, -1, -1, MotivoMovimiento.AJUSTE_MANUAL, DateTime.UtcNow));
    }
}
