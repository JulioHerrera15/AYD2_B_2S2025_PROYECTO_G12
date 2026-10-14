using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// Retencion de stock mientras el producto esta en el carrito: las unidades
/// retenidas suman a la reservada de la existencia y cada cambio mueve solo la
/// diferencia.
/// </summary>
public sealed class ServicioReservasCarritoTests
{
    private const string Carrito = "cart:6f1c2a7e-0000-4000-8000-000000000001";
    private const string Sku = "PROD-101";
    private const string OtroSku = "PROD-105";

    private static readonly Sucursal Central = new("TIENDA-01-CENTRAL", "MegaStore Central", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.6010, -90.5130);
    private static readonly Sucursal Oakland = new("TIENDA-05-OAKLAND", "MegaStore Oakland Mall", TipoUbicacion.TIENDA, "Guatemala", "Guatemala", "Guatemala", 14.5985, -90.5075);
    private static readonly Sucursal Xela = new("TIENDA-17-XELA", "MegaStore Multiplaza", TipoUbicacion.TIENDA, "El Salvador", "La Libertad", "Antiguo Cuscatlán", 13.6785, -89.2520);

    private static Existencia En(Sucursal sucursal, int fisica, string sku = Sku) =>
        new(sku, sucursal.Id, sucursal.TipoUbicacion, fisica);

    private static (ServicioReservasCarrito Servicio, RepositorioReservasCarritoFalso Retenciones) Construir(
        IEnumerable<ReservaCarrito>? retenciones = null,
        params Existencia[] existencias)
    {
        var repoRetenciones = new RepositorioReservasCarritoFalso().Precargar((retenciones ?? Array.Empty<ReservaCarrito>()).ToArray());

        var servicio = new ServicioReservasCarrito(
            repoRetenciones,
            new RepositorioExistenciasFalso().Precargar(existencias),
            new RepositorioSucursalesFalso().Precargar(Central, Oakland, Xela),
            Options.Create(new InventarioOptions { CarritoTtlSegundos = 900 }),
            NullLogger<ServicioReservasCarrito>.Instance);

        return (servicio, repoRetenciones);
    }

    /// <summary>Retencion ACTIVA creada hace <paramref name="minutosAtras" /> minutos, con su existencia ya reservada.</summary>
    private static ReservaCarrito RetencionPrevia(Existencia existencia, int cantidad, int minutosAtras)
    {
        existencia.Reservar(cantidad);
        return ReservaCarrito.Crear(
            Carrito,
            existencia.Sku,
            existencia.UbicacionId,
            existencia.TipoUbicacion,
            cantidad,
            existencia.UbicacionId,
            900,
            DateTime.UtcNow.AddMinutes(-minutosAtras));
    }

    [Fact]
    public async Task Crear_una_retencion_incrementa_la_reservada_de_la_existencia()
    {
        var central = En(Central, 5);
        var (servicio, retenciones) = Construir(existencias: central);

        var linea = await servicio.RetenerAsync(Carrito, Sku, 2, Central.Id, CancellationToken.None);

        Assert.Equal(2, central.CantidadReservada);
        Assert.Equal(3, central.Disponible);
        Assert.Equal(1, retenciones.LlamadasAgregar);

        Assert.Equal(Central.Id, linea.UbicacionId);
        Assert.Equal("MegaStore Central", linea.NombreUbicacion);
        Assert.Equal(2, linea.Cantidad);
        Assert.Equal("ACTIVA", linea.Estado);
        Assert.False(linea.EnOtraSucursal);
        Assert.True(linea.ExpiraEn > DateTime.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task Ajustar_la_cantidad_aplica_solo_el_delta_sobre_la_existencia()
    {
        var central = En(Central, 5);
        var (servicio, retenciones) = Construir(existencias: central);

        await servicio.RetenerAsync(Carrito, Sku, 2, Central.Id, CancellationToken.None);
        await servicio.RetenerAsync(Carrito, Sku, 3, Central.Id, CancellationToken.None);

        Assert.Equal(3, central.CantidadReservada);

        var linea = await servicio.RetenerAsync(Carrito, Sku, 1, Central.Id, CancellationToken.None);

        Assert.Equal(1, central.CantidadReservada);
        Assert.Equal(4, central.Disponible);
        Assert.Equal(1, linea.Cantidad);

        var unica = Assert.Single(retenciones.Todas);
        Assert.Equal(EstadoReservaCarrito.ACTIVA, unica.Estado);
    }

    [Fact]
    public async Task Si_la_sucursal_preferida_no_alcanza_retiene_en_la_tienda_mas_cercana_y_lo_marca()
    {
        var central = En(Central, 0);
        var oakland = En(Oakland, 2);
        var xela = En(Xela, 2);
        var (servicio, _) = Construir(existencias: new[] { central, oakland, xela });

        var linea = await servicio.RetenerAsync(Carrito, Sku, 2, Central.Id, CancellationToken.None);

        Assert.Equal(Oakland.Id, linea.UbicacionId);
        Assert.True(linea.EnOtraSucursal);
        Assert.Equal(2, oakland.CantidadReservada);
        Assert.Equal(0, xela.CantidadReservada);
    }

    [Fact]
    public async Task Sin_stock_en_ninguna_parte_lanza_sin_tocar_existencias_ni_retenciones()
    {
        var central = En(Central, 0);
        var oakland = En(Oakland, 0);
        var (servicio, retenciones) = Construir(existencias: new[] { central, oakland });

        var ex = await Assert.ThrowsAsync<ExcepcionSinStockCarrito>(
            () => servicio.RetenerAsync(Carrito, Sku, 1, Central.Id, CancellationToken.None));

        Assert.Equal(Sku, ex.ACuerpo().Sku);
        Assert.Equal(0, ex.ACuerpo().Disponible);
        Assert.Null(ex.ACuerpo().SucursalCercana);
        Assert.Equal(0, central.CantidadReservada);
        Assert.Empty(retenciones.Todas);
    }

    [Fact]
    public async Task Renovar_extiende_ExpiraEn_de_todas_las_lineas_activas()
    {
        var central = En(Central, 5);
        var centralOtro = En(Central, 5, OtroSku);
        var primera = RetencionPrevia(central, 1, minutosAtras: 10);
        var segunda = RetencionPrevia(centralOtro, 2, minutosAtras: 12);
        var expiraAntesPrimera = primera.ExpiraEn;
        var expiraAntesSegunda = segunda.ExpiraEn;

        var (servicio, _) = Construir(new[] { primera, segunda }, central, centralOtro);

        var carrito = await servicio.RenovarAsync(Carrito, skus: null, CancellationToken.None);

        Assert.True(primera.ExpiraEn > expiraAntesPrimera);
        Assert.True(segunda.ExpiraEn > expiraAntesSegunda);
        Assert.Equal(primera.ExpiraEn, segunda.ExpiraEn);
        Assert.Equal(2, carrito.Lineas.Count);
        Assert.All(carrito.Lineas, linea => Assert.Equal("ACTIVA", linea.Estado));
        Assert.Equal(primera.ExpiraEn, carrito.ExpiraEn);

        // Renovar no mueve stock: las mismas unidades siguen retenidas.
        Assert.Equal(1, central.CantidadReservada);
        Assert.Equal(2, centralOtro.CantidadReservada);
    }

    [Fact]
    public async Task Cualquier_cambio_en_el_carrito_renueva_tambien_las_demas_lineas()
    {
        var central = En(Central, 5);
        var centralOtro = En(Central, 5, OtroSku);
        var previa = RetencionPrevia(centralOtro, 1, minutosAtras: 14);
        var expiraAntes = previa.ExpiraEn;

        var (servicio, _) = Construir(new[] { previa }, central, centralOtro);

        var nueva = await servicio.RetenerAsync(Carrito, Sku, 1, Central.Id, CancellationToken.None);

        Assert.True(previa.ExpiraEn > expiraAntes);
        Assert.Equal(nueva.ExpiraEn, previa.ExpiraEn);
    }

    [Fact]
    public async Task Renovar_una_linea_expirada_sin_stock_la_devuelve_expirada_con_la_tienda_cercana_sugerida()
    {
        var central = En(Central, 1);
        var oakland = En(Oakland, 3);
        var vencida = RetencionPrevia(central, 1, minutosAtras: 20);
        central.LiberarReserva(1);
        vencida.MarcarExpirada();

        // Otro cliente se llevo la unidad mientras la retencion estaba vencida.
        central.Reservar(1);

        var (servicio, _) = Construir(new[] { vencida }, central, oakland);

        var carrito = await servicio.RenovarAsync(Carrito, new[] { Sku }, CancellationToken.None);

        var linea = Assert.Single(carrito.Lineas);
        Assert.Equal("EXPIRADA", linea.Estado);
        Assert.Equal(Oakland.Id, linea.SucursalSugerida?.UbicacionId);
        Assert.Null(carrito.ExpiraEn);
        Assert.Equal(1, central.CantidadReservada);
        Assert.Equal(0, oakland.CantidadReservada);
    }

    [Fact]
    public async Task Liberar_un_item_devuelve_sus_unidades_y_cierra_la_linea()
    {
        var central = En(Central, 5);
        var previa = RetencionPrevia(central, 3, minutosAtras: 1);
        var (servicio, _) = Construir(new[] { previa }, central);

        var liberada = await servicio.LiberarItemAsync(Carrito, Sku, CancellationToken.None);

        Assert.True(liberada);
        Assert.Equal(0, central.CantidadReservada);
        Assert.Equal(EstadoReservaCarrito.LIBERADA, previa.Estado);
    }
}
