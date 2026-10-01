using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;
using Xunit;

namespace SgoLogic.Inventario.Tests.Domain;

public sealed class ExistenciaTests
{
    private static Existencia Nueva(int fisica = 10) =>
        new("SKU-1", "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, fisica);

    [Fact]
    public void Reservar_baja_el_disponible_y_sube_la_version()
    {
        var existencia = Nueva(fisica: 10);

        existencia.Reservar(4);

        Assert.Equal(10, existencia.CantidadFisica);
        Assert.Equal(4, existencia.CantidadReservada);
        Assert.Equal(6, existencia.Disponible);
        Assert.Equal(1, existencia.Version);
    }

    [Fact]
    public void Reservar_mas_que_el_disponible_lanza_y_no_modifica_nada()
    {
        var existencia = Nueva(fisica: 3);

        var ex = Assert.Throws<ExcepcionStockInsuficiente>(() => existencia.Reservar(5));

        Assert.Equal(5, ex.Solicitado);
        Assert.Equal(3, ex.Disponible);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Equal(0, existencia.Version);
    }

    [Fact]
    public void Reservar_hasta_el_ultimo_disponible_no_permite_sobreventa()
    {
        var existencia = Nueva(fisica: 2);

        existencia.Reservar(2);

        Assert.Equal(0, existencia.Disponible);
        Assert.Throws<ExcepcionStockInsuficiente>(() => existencia.Reservar(1));
        Assert.Equal(2, existencia.CantidadReservada);
    }

    [Fact]
    public void Liberar_devuelve_unidades_al_disponible_sin_bajar_de_cero()
    {
        var existencia = Nueva(fisica: 10);
        existencia.Reservar(6);

        existencia.LiberarReserva(4);
        Assert.Equal(2, existencia.CantidadReservada);
        Assert.Equal(8, existencia.Disponible);

        existencia.LiberarReserva(999);
        Assert.Equal(0, existencia.CantidadReservada);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Reservar_cantidad_no_positiva_lanza(int cantidad)
    {
        var existencia = Nueva();
        Assert.Throws<ArgumentOutOfRangeException>(() => existencia.Reservar(cantidad));
    }

    [Fact]
    public void AjustarCantidadFisica_por_debajo_de_lo_reservado_lanza_y_no_modifica_nada()
    {
        var existencia = Nueva(fisica: 10);
        existencia.Reservar(4);
        var versionAntes = existencia.Version;

        var ex = Assert.Throws<ExcepcionCantidadFisicaMenorQueReservada>(() => existencia.AjustarCantidadFisica(3));

        Assert.Equal(3, ex.CantidadFisica);
        Assert.Equal(4, ex.CantidadReservada);
        Assert.Equal(10, existencia.CantidadFisica);
        Assert.Equal(4, existencia.CantidadReservada);
        Assert.Equal(versionAntes, existencia.Version);
    }

    [Fact]
    public void AjustarCantidadFisica_igual_a_lo_reservado_se_acepta_y_deja_disponible_en_cero()
    {
        var existencia = Nueva(fisica: 10);
        existencia.Reservar(4);

        existencia.AjustarCantidadFisica(4);

        Assert.Equal(4, existencia.CantidadFisica);
        Assert.Equal(0, existencia.Disponible);
    }
}
