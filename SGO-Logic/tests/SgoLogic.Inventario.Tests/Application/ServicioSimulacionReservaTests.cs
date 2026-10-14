using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>Simulacion de la reserva: desde donde saldria el pedido, sin reservar nada.</summary>
public sealed class ServicioSimulacionReservaTests
{
    private const string Sku = "PROD-105";
    private const string Carrito = "cart:6f1c2a7e-0000-4000-8000-000000000003";

    private static readonly Sucursal CentroDistribucion = new("CD-CENTRAL", "Centro de Distribución Central", TipoUbicacion.CENTRO_DISTRIBUCION, "Guatemala", "Guatemala", "Guatemala", 14.5860, -90.5420);
    private static readonly Sucursal Central = new("TIENDA-01-CENTRAL", "MegaStore Central", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.6010, -90.5130);
    private static readonly Sucursal Americas = new("TIENDA-12-AMERICAS", "MegaStore Plaza Las Américas", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.5880, -90.5250);

    private static Existencia En(Sucursal sucursal, int fisica) => new(Sku, sucursal.Id, sucursal.TipoUbicacion, fisica);

    private static ServicioSimulacionReserva Construir(RepositorioReservasCarritoFalso? retenciones = null, params Existencia[] existencias) =>
        new(
            new RepositorioExistenciasFalso().Precargar(existencias),
            retenciones ?? new RepositorioReservasCarritoFalso(),
            new RepositorioSucursalesFalso().Precargar(CentroDistribucion, Central, Americas));

    private static SimularReservaRequest Solicitud(
        MetodoEntrega metodo,
        string? preferida = null,
        string? referencia = null,
        string? carritoId = null,
        int cantidad = 2) =>
        new(carritoId, metodo, preferida, referencia, new[] { new LineaSolicitudReserva(Sku, cantidad) });

    [Fact]
    public async Task Domicilio_prioriza_el_centro_de_distribucion_y_no_lo_marca_como_otra_sucursal()
    {
        var servicio = Construir(existencias: new[] { En(CentroDistribucion, 2), En(Central, 2), En(Americas, 2) });

        var resultado = await servicio.SimularAsync(
            Solicitud(MetodoEntrega.DOMICILIO, referencia: Central.Id),
            CancellationToken.None);

        Assert.True(resultado.Factible);
        Assert.Equal(CentroDistribucion.Id, resultado.UbicacionId);
        Assert.Equal(TipoUbicacion.CENTRO_DISTRIBUCION, resultado.TipoUbicacion);
        Assert.False(resultado.EsOtraSucursal);
    }

    [Fact]
    public async Task Domicilio_que_sale_de_una_tienda_distinta_a_la_del_cliente_se_marca_como_otra_sucursal()
    {
        var servicio = Construir(existencias: new[] { En(CentroDistribucion, 0), En(Central, 0), En(Americas, 2) });

        var resultado = await servicio.SimularAsync(
            Solicitud(MetodoEntrega.DOMICILIO, referencia: Central.Id),
            CancellationToken.None);

        Assert.True(resultado.Factible);
        Assert.Equal(Americas.Id, resultado.UbicacionId);
        Assert.Equal("MegaStore Plaza Las Américas", resultado.NombreUbicacion);
        Assert.Equal(TipoUbicacion.TIENDA, resultado.TipoUbicacion);
        Assert.True(resultado.EsOtraSucursal);
    }

    [Fact]
    public async Task Recogida_en_una_tienda_que_no_tiene_todo_no_es_factible_y_sugiere_otra()
    {
        var servicio = Construir(existencias: new[] { En(Central, 1), En(Americas, 2) });

        var resultado = await servicio.SimularAsync(
            Solicitud(MetodoEntrega.RECOGIDA_TIENDA, preferida: Central.Id, referencia: Central.Id),
            CancellationToken.None);

        Assert.False(resultado.Factible);
        Assert.Equal(Americas.Id, resultado.UbicacionId);
        Assert.True(resultado.EsOtraSucursal);
        Assert.NotNull(resultado.Motivo);
    }

    [Fact]
    public async Task Las_retenciones_activas_del_carrito_cuentan_como_disponibles_y_no_se_modifica_nada()
    {
        var central = En(Central, 2);
        central.Reservar(2);
        var retencion = ReservaCarrito.Crear(Carrito, Sku, Central.Id, TipoUbicacion.TIENDA, 2, Central.Id, 900, DateTime.UtcNow);
        var servicio = Construir(new RepositorioReservasCarritoFalso().Precargar(retencion), central);

        var resultado = await servicio.SimularAsync(
            Solicitud(MetodoEntrega.RECOGIDA_TIENDA, preferida: Central.Id, referencia: Central.Id, carritoId: Carrito),
            CancellationToken.None);

        Assert.True(resultado.Factible);
        Assert.Equal(Central.Id, resultado.UbicacionId);
        Assert.False(resultado.EsOtraSucursal);
        Assert.Equal(2, central.CantidadReservada);
        Assert.Equal(EstadoReservaCarrito.ACTIVA, retencion.Estado);
    }

    [Fact]
    public async Task Sin_ninguna_ubicacion_que_cubra_el_pedido_no_es_factible_y_no_trae_ubicacion()
    {
        var servicio = Construir(existencias: new[] { En(CentroDistribucion, 1), En(Central, 1) });

        var resultado = await servicio.SimularAsync(Solicitud(MetodoEntrega.DOMICILIO), CancellationToken.None);

        Assert.False(resultado.Factible);
        Assert.Null(resultado.UbicacionId);
        Assert.NotNull(resultado.Motivo);
    }
}
