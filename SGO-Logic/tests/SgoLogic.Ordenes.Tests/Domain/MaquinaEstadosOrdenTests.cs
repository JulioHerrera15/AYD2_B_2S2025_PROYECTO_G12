using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Domain;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Domain;

public sealed class MaquinaEstadosOrdenTests
{
    private static Orden CrearOrden() => new(
        "ORD-2026-000123",
        "000123",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        150.00m);

    [Fact]
    public void Orden_nueva_arranca_en_CREADA_con_secuencia_uno()
    {
        var orden = CrearOrden();

        Assert.Equal(EstadoOrden.CREADA, orden.Estado);
        Assert.Equal(1, orden.Secuencia);
        Assert.Empty(orden.Transiciones);
        Assert.Equal(1400.00m, orden.Total);
    }

    [Fact]
    public void Camino_feliz_completo_llega_a_ENTREGADA()
    {
        var orden = CrearOrden();

        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA, pasoSaga: PasoSaga.RESERVA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO, pasoSaga: PasoSaga.RESERVA);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO, pasoSaga: PasoSaga.COBRO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO, pasoSaga: PasoSaga.COBRO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, pasoSaga: PasoSaga.FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA, pasoSaga: PasoSaga.FISCAL);
        orden.TransicionarA(EstadoOrden.DESPACHO_SOLICITADO, pasoSaga: PasoSaga.DESPACHO);
        orden.TransicionarA(EstadoOrden.EN_PREPARACION, pasoSaga: PasoSaga.DESPACHO);
        orden.TransicionarA(EstadoOrden.EN_TRANSITO, pasoSaga: PasoSaga.DESPACHO);
        orden.TransicionarA(EstadoOrden.ENTREGADA, pasoSaga: PasoSaga.DESPACHO);

        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);
        Assert.Equal(10, orden.Transiciones.Count);
        Assert.Equal(11, orden.Secuencia);
    }

    [Fact]
    public void La_secuencia_de_cada_transicion_es_monotonica_y_sin_huecos()
    {
        var orden = CrearOrden();

        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);

        var secuencias = orden.Transiciones.Select(transicion => transicion.Secuencia).ToArray();

        Assert.Equal(new[] { 2, 3, 4 }, secuencias);
    }

    [Theory]
    [InlineData(EstadoOrden.CREADA, EstadoOrden.CONFIRMADA)]
    [InlineData(EstadoOrden.CREADA, EstadoOrden.ENTREGADA)]
    [InlineData(EstadoOrden.CREADA, EstadoOrden.PAGO_APLICADO)]
    public void Transicion_invalida_desde_CREADA_lanza(EstadoOrden desde, EstadoOrden hacia)
    {
        var orden = CrearOrden();

        Assert.Equal(desde, orden.Estado);
        var excepcion = Assert.Throws<ExcepcionTransicionInvalida>(() => orden.TransicionarA(hacia));
        Assert.Equal(desde, excepcion.Desde);
        Assert.Equal(hacia, excepcion.Hacia);
    }

    [Fact]
    public void Una_orden_cancelada_no_admite_ninguna_transicion()
    {
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.CANCELADA, "Cancelada por el cliente");

        Assert.True(orden.EsTerminal);
        Assert.Throws<ExcepcionTransicionInvalida>(() => orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA));
    }

    [Fact]
    public void Pago_pendiente_permite_reintento_de_cobro_y_marca_compensacion()
    {
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);

        var compensacion = orden.TransicionarA(
            EstadoOrden.PAGO_PENDIENTE,
            "PayKlik no respondio dentro del timeout configurado",
            PasoSaga.COBRO,
            esCompensacion: true);

        Assert.True(compensacion.EsCompensacion);
        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, compensacion.EstadoAnterior);

        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO, "Reintento por enlace de pago");

        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, orden.Estado);
    }

    [Fact]
    public void Solo_cuatro_estados_son_terminales()
    {
        var terminales = Enum.GetValues<EstadoOrden>()
            .Where(MaquinaEstadosOrden.EsTerminal)
            .OrderBy(estado => estado.ToString())
            .ToArray();

        Assert.Equal(
            new[]
            {
                EstadoOrden.CANCELADA,
                EstadoOrden.CANCELADA_RESERVA_VENCIDA,
                EstadoOrden.DEVUELTA,
                EstadoOrden.RECHAZADA
            }.OrderBy(estado => estado.ToString()).ToArray(),
            terminales);
    }

    [Fact]
    public void Recogida_en_tienda_exige_ubicacion()
    {
        Assert.Throws<ArgumentException>(() => new Orden(
            "ORD-2026-000124",
            "000124",
            "CLI-000045",
            Canal.TIENDA,
            MetodoEntrega.RECOGIDA_TIENDA,
            Moneda.GTQ,
            MedioPago.TARJETA,
            new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
            150.00m));
    }

    [Theory]
    [InlineData("REQ-ID-001")]
    [InlineData("3f5b2c9e-8a41-4d7e-9c02-1b6e5a7d4f30")]
    [InlineData("ORD-2026-12345")]
    [InlineData("ORD-26-000123")]
    [InlineData("ord-2026-000123")]
    public void Un_id_que_no_cumple_el_patron_ORD_anio_correlativo_es_rechazado(string idInvalido)
    {
        var excepcion = Assert.Throws<ArgumentException>(() => new Orden(
            idInvalido,
            "000123",
            "CLI-000045",
            Canal.WEB,
            MetodoEntrega.DOMICILIO,
            Moneda.GTQ,
            MedioPago.TARJETA,
            new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
            150.00m));

        Assert.Equal("id", excepcion.ParamName);
    }
}
