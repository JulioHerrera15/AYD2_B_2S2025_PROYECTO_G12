using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SgoLogic.Auth.Api.Application;
using SgoLogic.Auth.Api.Controllers;
using SgoLogic.Auth.Api.Infrastructure;
using SgoLogic.Auth.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Auth.Tests.Application;

/// <summary>
/// El carrito pasa primero por Inventario: si Inventario rechaza (409) o no
/// responde (503), Redis no se toca.
/// </summary>
public sealed class CarritoServiceTests
{
    private const string Sesion = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string Clave = "cart:" + Sesion;
    private const string TiendaCentral = "TIENDA-01-CENTRAL";

    private static readonly ItemCarritoDto ItemPrevio = new("PROD-105", "Cámara de Seguridad Wi-Fi 2K", 1, 699.00m);

    private readonly string _dbName = Guid.NewGuid().ToString();

    private (CarritoService Servicio, AlmacenCarritoFalso Almacen, ClienteInventarioCarritoFalso Inventario) Construir()
    {
        var contexto = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

        var almacen = new AlmacenCarritoFalso().Precargar(Clave, ItemPrevio);
        var inventario = new ClienteInventarioCarritoFalso();
        var servicio = new CarritoService(almacen, contexto, inventario, new ConfigurationBuilder().Build());

        return (servicio, almacen, inventario);
    }

    private static AgregarItemRequest Agregar(string sku = "PROD-104", int cantidad = 1) =>
        new(sku, "Deshumidificador Inteligente 30L", cantidad, 2199.00m, TiendaCentral);

    [Fact]
    public async Task Agregar_item_con_Inventario_en_409_no_modifica_Redis_y_conserva_el_cuerpo_del_rechazo()
    {
        var (servicio, almacen, inventario) = Construir();
        inventario.RechazarSinStock = true;

        var ex = await Assert.ThrowsAsync<ExcepcionInventarioRechazo>(
            () => servicio.AgregarItemAsync(Sesion, Agregar()));

        Assert.Equal(409, ex.Status);
        Assert.Equal(ClienteInventarioCarritoFalso.CuerpoSinStock, ex.Cuerpo);
        Assert.Equal(0, almacen.Escrituras);
        Assert.Equal(new[] { ItemPrevio }, almacen.Contenido(Clave));
    }

    [Fact]
    public async Task Agregar_item_con_Inventario_caido_lanza_no_disponible_y_no_modifica_Redis()
    {
        var (servicio, almacen, inventario) = Construir();
        inventario.Caido = true;

        await Assert.ThrowsAsync<ExcepcionInventarioNoDisponible>(
            () => servicio.AgregarItemAsync(Sesion, Agregar()));

        Assert.Equal(0, almacen.Escrituras);
        Assert.Equal(new[] { ItemPrevio }, almacen.Contenido(Clave));
    }

    [Fact]
    public async Task Agregar_un_item_existente_retiene_la_cantidad_total_y_guarda_donde_quedo_apartado()
    {
        var (servicio, almacen, inventario) = Construir();

        await servicio.AgregarItemAsync(Sesion, Agregar("PROD-105", cantidad: 2));

        var retencion = Assert.Single(inventario.Retenciones);
        Assert.Equal((Clave, "PROD-105", 3, TiendaCentral), retencion);

        var item = Assert.Single(almacen.Contenido(Clave)!);
        Assert.Equal(3, item.Cantidad);
        Assert.Equal(TiendaCentral, item.UbicacionId);
        Assert.Equal("ACTIVA", item.EstadoReserva);
        Assert.NotNull(item.ExpiraEn);
    }

    [Fact]
    public async Task Eliminar_item_con_Inventario_caido_no_modifica_Redis()
    {
        var (servicio, almacen, inventario) = Construir();
        inventario.Caido = true;

        await Assert.ThrowsAsync<ExcepcionInventarioNoDisponible>(
            () => servicio.EliminarItemAsync(Sesion, "PROD-105"));

        Assert.Equal(0, almacen.Escrituras);
        Assert.Equal(0, almacen.Borrados);
        Assert.Equal(new[] { ItemPrevio }, almacen.Contenido(Clave));
    }

    [Fact]
    public async Task Obtener_con_Inventario_caido_devuelve_lo_que_hay_en_Redis()
    {
        var (servicio, _, inventario) = Construir();
        inventario.Caido = true;

        var carrito = await servicio.ObtenerAsync(Sesion);

        Assert.Equal(Clave, carrito.CarritoId);
        Assert.Single(carrito.Items);
    }

    // ── Controlador: traduccion a 409 y 503 ────────────────────────────────────

    private static CarritoController Controlador(CarritoService servicio)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Session-Id"] = Sesion;

        return new CarritoController(servicio)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    [Fact]
    public async Task POST_items_con_Inventario_caido_responde_503_y_el_carrito_no_cambia()
    {
        var (servicio, almacen, inventario) = Construir();
        inventario.Caido = true;

        var resultado = await Controlador(servicio).AgregarItem(Agregar(), CancellationToken.None);

        var objeto = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objeto.StatusCode);
        Assert.IsType<ProblemDetails>(objeto.Value);
        Assert.Equal(0, almacen.Escrituras);
    }

    [Fact]
    public async Task POST_items_con_Inventario_en_409_responde_409_con_el_mismo_cuerpo()
    {
        var (servicio, almacen, inventario) = Construir();
        inventario.RechazarSinStock = true;

        var resultado = await Controlador(servicio).AgregarItem(Agregar(), CancellationToken.None);

        var contenido = Assert.IsType<ContentResult>(resultado);
        Assert.Equal(StatusCodes.Status409Conflict, contenido.StatusCode);
        Assert.Equal(ClienteInventarioCarritoFalso.CuerpoSinStock, contenido.Content);
        Assert.Equal(0, almacen.Escrituras);
    }

    // ── B1: el precio y la descripcion salen de Inventario ────────────────────

    [Fact]
    public async Task Agregar_item_ignora_el_precio_y_la_descripcion_que_manda_el_cliente()
    {
        var (servicio, almacen, inventario) = Construir();

        await servicio.AgregarItemAsync(Sesion, new AgregarItemRequest("PROD-104", "Producto barato", 1, 0.01m, TiendaCentral));

        var item = Assert.Single(almacen.Contenido(Clave)!, i => i.Sku == "PROD-104");
        Assert.Equal(2199.00m, item.PrecioUnitario);
        Assert.Equal("Deshumidificador Inteligente 30L", item.Descripcion);
        Assert.Contains("PROD-104", inventario.ConsultasProducto);
    }

    [Fact]
    public async Task Actualizar_cantidad_refresca_el_precio_vigente_de_Inventario()
    {
        var (servicio, almacen, inventario) = Construir();
        inventario.Productos["PROD-105"] = inventario.Productos["PROD-105"] with { Precio = 749.00m };

        await servicio.ActualizarCantidadItemAsync(Sesion, "PROD-105", 2);

        var item = Assert.Single(almacen.Contenido(Clave)!);
        Assert.Equal(2, item.Cantidad);
        Assert.Equal(749.00m, item.PrecioUnitario);
    }

    [Fact]
    public async Task POST_items_con_sku_que_no_existe_en_Inventario_responde_404_y_no_retiene_stock()
    {
        var (servicio, almacen, inventario) = Construir();

        var resultado = await Controlador(servicio).AgregarItem(Agregar("SKU-INEXISTENTE"), CancellationToken.None);

        var noEncontrado = Assert.IsType<NotFoundObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status404NotFound, noEncontrado.StatusCode);
        Assert.Empty(inventario.Retenciones);
        Assert.Equal(0, almacen.Escrituras);
    }
}
