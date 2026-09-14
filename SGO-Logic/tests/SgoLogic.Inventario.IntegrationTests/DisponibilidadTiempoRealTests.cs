using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;

namespace SgoLogic.Inventario.IntegrationTests;

/// <summary>
/// HU-017 (EV-08, EV-32): la consulta de disponibilidad refleja el stock
/// actualizado post-reservas, contra PostgreSQL real.
///
/// Cada operacion corre en su propio alcance de inyeccion, o sea con un
/// <see cref="InventarioDbContext" /> nuevo. Eso es deliberado: si las lecturas
/// compartieran contexto con la escritura, el change tracker de EF Core las
/// respondería de memoria y el test pasaria sin haber consultado la base. Alcances
/// separados imitan peticiones HTTP distintas y obligan a que cada lectura viaje a
/// Postgres, que es justo lo que el criterio de aceptacion exige demostrar.
/// </summary>
[Collection("inventario-integracion")]
public sealed class DisponibilidadTiempoRealTests
{
    private readonly InventarioFixture _fixture;

    public DisponibilidadTiempoRealTests(InventarioFixture fixture)
    {
        _fixture = fixture;
    }

    private static string SkuNuevo() => $"SKU-INT-{Guid.NewGuid():N}"[..20];

    private static string OrdenNueva() => $"ORD-INT-{Guid.NewGuid():N}"[..20];

    private async Task CargarExistenciaAsync(
        ServiceProvider proveedor,
        string sku,
        string ubicacion,
        TipoUbicacion tipo,
        int cantidad)
    {
        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioExistencias>();

        await servicio.RegistrarAsync(
            new RegistrarExistenciaRequest(sku, ubicacion, tipo, cantidad),
            CancellationToken.None);
    }

    private async Task<ResultadoReservaDto> ReservarAsync(
        ServiceProvider proveedor,
        string ordenId,
        string sku,
        string ubicacion,
        int cantidad)
    {
        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioReservas>();
        var unidadDeTrabajo = alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>();

        var resultado = await servicio.ReservarAsync(
            new SolicitudReserva(
                ordenId,
                MetodoEntrega.RECOGIDA_TIENDA,
                ubicacion,
                900,
                new[] { new LineaSolicitudReserva(sku, cantidad) }),
            CancellationToken.None);

        await unidadDeTrabajo.GuardarCambiosAsync(CancellationToken.None);
        return resultado;
    }

    private async Task<ExistenciaDto> LeerAsync(ServiceProvider proveedor, string sku)
    {
        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioExistencias>();

        var existencias = await servicio.ListarPorSkuAsync(sku, CancellationToken.None);
        return Assert.Single(existencias);
    }

    [Fact]
    public async Task Reservar_baja_el_disponible_en_la_lectura_inmediata_siguiente()
    {
        // Este es el criterio de aceptacion de HU-017: la consulta refleja el stock
        // post-reserva sin esperar ningun proceso asincrono.
        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();

        await CargarExistenciaAsync(proveedor, sku, "CD-INT-01", TipoUbicacion.CENTRO_DISTRIBUCION, 10);

        var antes = await LeerAsync(proveedor, sku);
        Assert.Equal(10, antes.Disponible);

        var resultado = await ReservarAsync(proveedor, OrdenNueva(), sku, "CD-INT-01", 4);
        Assert.False(resultado.Rechazada);

        var despues = await LeerAsync(proveedor, sku);

        // El disponible baja exactamente lo reservado...
        Assert.Equal(6, despues.Disponible);

        // ...y la cantidad fisica no se toca: la reserva es un compromiso, no una salida.
        Assert.Equal(10, despues.CantidadFisica);
        Assert.Equal(4, despues.CantidadReservada);

        // El token de concurrencia avanzo, que es lo que protege contra sobreventa.
        Assert.True(despues.Version > antes.Version);
    }

    [Fact]
    public async Task La_consulta_por_ubicacion_refleja_el_disponible_post_reserva()
    {
        // El angulo "por ubicacion" de HU-017: un vendedor en la tienda X quiere ver
        // que tiene disponible ahi para decidir desde donde despachar.
        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();

        await CargarExistenciaAsync(proveedor, sku, "TDA-INT-07", TipoUbicacion.TIENDA, 8);
        await ReservarAsync(proveedor, OrdenNueva(), sku, "TDA-INT-07", 3);

        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioExistencias>();

        var enLaTienda = await servicio.ListarPorUbicacionAsync("TDA-INT-07", CancellationToken.None);
        var existencia = Assert.Single(enLaTienda.Where(e => e.Sku == sku));

        Assert.Equal(5, existencia.Disponible);
        Assert.Equal(8, existencia.CantidadFisica);
    }

    [Fact]
    public async Task El_resumen_de_ubicaciones_se_traduce_a_SQL_y_descuenta_lo_reservado()
    {
        // Regresion: la version original de ResumirUbicacionesAsync proyectaba el
        // GroupBy directo al constructor del DTO y EF Core no sabia traducirlo, asi
        // que el endpoint respondia 500. Los dobles en memoria no podian detectarlo
        // porque LINQ-to-Objects traduce cualquier cosa; solo Postgres lo revela.
        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        var ubicacion = $"TDA-RES-{Guid.NewGuid():N}"[..12];

        await CargarExistenciaAsync(proveedor, sku, ubicacion, TipoUbicacion.TIENDA, 12);
        await ReservarAsync(proveedor, OrdenNueva(), sku, ubicacion, 5);

        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioExistencias>();

        var ubicaciones = await servicio.ListarUbicacionesAsync(CancellationToken.None);
        var resumen = Assert.Single(ubicaciones.Where(u => u.UbicacionId == ubicacion));

        Assert.Equal(TipoUbicacion.TIENDA, resumen.TipoUbicacion);
        Assert.Equal(1, resumen.CantidadDeSkus);
        Assert.Equal(12, resumen.CantidadTotalFisica);
        Assert.Equal(5, resumen.CantidadTotalReservada);
        Assert.Equal(7, resumen.TotalDisponible);
    }

    [Fact]
    public async Task Al_expirar_la_reserva_el_disponible_vuelve_a_subir()
    {
        // El otro extremo de la propiedad: HU-002 devuelve las unidades y la consulta
        // lo refleja en la misma transaccion, sin ventana de inconsistencia.
        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        var ordenId = OrdenNueva();

        await CargarExistenciaAsync(proveedor, sku, "CD-INT-02", TipoUbicacion.CENTRO_DISTRIBUCION, 10);
        await ReservarAsync(proveedor, ordenId, sku, "CD-INT-02", 4);

        Assert.Equal(6, (await LeerAsync(proveedor, sku)).Disponible);

        // Se adelanta el reloj de la reserva en la base en vez de esperar el TTL
        // minimo real (60 s): lo que se prueba es el efecto del vencimiento, no la
        // puntualidad del reloj.
        await using (var contexto = InventarioServicios.CrearDbContext(_fixture.CadenaConexion))
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "UPDATE inventario.reserva SET expira_en = now() - interval '1 minute' WHERE orden_id = {0}",
                ordenId);
        }

        using (var alcance = proveedor.CreateScope())
        {
            var expiracion = alcance.ServiceProvider.GetRequiredService<ServicioExpiracionReservas>();
            var procesadas = await expiracion.ProcesarLoteAsync(CancellationToken.None);
            Assert.True(procesadas >= 1);
        }

        var despues = await LeerAsync(proveedor, sku);

        Assert.Equal(10, despues.Disponible);
        Assert.Equal(0, despues.CantidadReservada);
        Assert.Equal(10, despues.CantidadFisica);
    }

    [Fact]
    public async Task Sin_disponible_la_consulta_nunca_reporta_negativo()
    {
        // No-sobreventa visto desde la lectura: agotar el disponible lo deja en cero,
        // y la reserva que sobra se rechaza en vez de empujarlo bajo cero.
        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();

        await CargarExistenciaAsync(proveedor, sku, "CD-INT-03", TipoUbicacion.CENTRO_DISTRIBUCION, 3);
        await ReservarAsync(proveedor, OrdenNueva(), sku, "CD-INT-03", 3);

        Assert.Equal(0, (await LeerAsync(proveedor, sku)).Disponible);

        var sobrante = await ReservarAsync(proveedor, OrdenNueva(), sku, "CD-INT-03", 1);
        Assert.True(sobrante.Rechazada);

        var despues = await LeerAsync(proveedor, sku);
        Assert.Equal(0, despues.Disponible);
        Assert.Equal(3, despues.CantidadReservada);
    }
}
