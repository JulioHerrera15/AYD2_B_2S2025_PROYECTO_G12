using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// HU-014: las unidades devueltas vuelven al stock de la ubicacion que corresponde,
/// y el ledger de movimientos lo explica.
/// </summary>
public sealed class ServicioReingresoStockTests
{
    private const string Sku = "SKU-DV-00110";
    private const string Ubicacion = "TDA-07";

    private static (ServicioReingresoStock Servicio,
        RepositorioExistenciasFalso Existencias,
        RepositorioMovimientosFalso Movimientos,
        OutboxWriterFalso Outbox) Construir(params Existencia[] existencias)
    {
        var repoExistencias = new RepositorioExistenciasFalso().Precargar(existencias);
        var movimientos = new RepositorioMovimientosFalso();
        var outbox = new OutboxWriterFalso();

        return (new ServicioReingresoStock(repoExistencias, movimientos, outbox, NullLogger<ServicioReingresoStock>.Instance),
            repoExistencias, movimientos, outbox);
    }

    private static SolicitudReingreso Solicitud(params LineaReingreso[] lineas) =>
        new("DEV-2026-000001", "ORD-2026-000001", Ubicacion, TipoUbicacion.TIENDA,
            lineas.Length == 0 ? new[] { new LineaReingreso(Sku, 2, null) } : lineas);

    [Fact]
    public async Task Reingresar_sube_la_cantidad_fisica_y_el_disponible()
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 5);
        var (servicio, _, _, _) = Construir(existencia);

        var reingreso = await servicio.ReingresarAsync(Solicitud(), CancellationToken.None);

        Assert.True(reingreso);

        // A diferencia de liberar una reserva, aqui si sube la fisica: es mercaderia
        // que volvio al anaquel.
        Assert.Equal(7, existencia.CantidadFisica);
        Assert.Equal(7, existencia.Disponible);
    }

    [Fact]
    public async Task El_reingreso_deja_asiento_en_el_ledger()
    {
        var (servicio, _, movimientos, _) = Construir(new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 5));

        await servicio.ReingresarAsync(Solicitud(), CancellationToken.None);

        var movimiento = Assert.Single(movimientos.Todos);
        Assert.Equal(TipoMovimiento.ENTRADA, movimiento.Tipo);
        Assert.Equal(MotivoMovimiento.REINGRESO_DEVOLUCION, movimiento.Motivo);
        Assert.Equal(2, movimiento.Cantidad);
        Assert.Equal(7, movimiento.CantidadFisicaResultante);
    }

    [Fact]
    public async Task Reingresar_encola_el_evento_con_el_saldo_resultante()
    {
        var (servicio, _, _, outbox) = Construir(new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 5));

        await servicio.ReingresarAsync(Solicitud(), CancellationToken.None);

        var evento = Assert.Single(outbox.PorTipo(StockReingresado.EventType));
        var datos = Assert.IsType<StockReingresadoData>(evento.Data);
        var linea = Assert.Single(datos.Lineas);

        Assert.Equal(Sku, linea.Sku);
        Assert.Equal(2, linea.Cantidad);
        Assert.Equal(7, linea.CantidadFisicaResultante);
    }

    [Fact]
    public async Task Cada_linea_vuelve_a_su_propia_ubicacion()
    {
        // El contrato v1.3 trae UbicacionOrigenReservaId por linea porque una orden
        // pudo despacharse desde varias ubicaciones.
        var enTienda = new Existencia(Sku, "TDA-07", TipoUbicacion.TIENDA, 1);
        var enCentro = new Existencia(Sku, "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, 10);
        var (servicio, _, _, _) = Construir(enTienda, enCentro);

        await servicio.ReingresarAsync(
            Solicitud(new LineaReingreso(Sku, 3, "CD-01")), CancellationToken.None);

        Assert.Equal(13, enCentro.CantidadFisica);
        Assert.Equal(1, enTienda.CantidadFisica);
    }

    [Fact]
    public async Task Sin_ubicacion_en_la_linea_se_usa_la_del_encabezado()
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 5);
        var (servicio, _, _, _) = Construir(existencia);

        await servicio.ReingresarAsync(
            Solicitud(new LineaReingreso(Sku, 1, "")), CancellationToken.None);

        Assert.Equal(6, existencia.CantidadFisica);
    }

    [Fact]
    public async Task Una_devolucion_en_otra_tienda_crea_la_existencia()
    {
        // La mercaderia vuelve a una ubicacion donde ese SKU nunca estuvo. Perder esas
        // unidades seria peor que crear la fila.
        var (servicio, existencias, _, _) = Construir();

        var reingreso = await servicio.ReingresarAsync(Solicitud(), CancellationToken.None);

        Assert.True(reingreso);
        Assert.Equal(1, existencias.LlamadasAgregar);

        var creada = Assert.Single(await existencias.ListarPorSkuAsync(Sku, CancellationToken.None));
        Assert.Equal(2, creada.CantidadFisica);
    }

    [Fact]
    public async Task Una_devolucion_sin_lineas_utiles_no_emite_nada()
    {
        var (servicio, _, movimientos, outbox) = Construir(new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 5));

        var reingreso = await servicio.ReingresarAsync(
            Solicitud(new LineaReingreso(Sku, 0, null)), CancellationToken.None);

        Assert.False(reingreso);
        Assert.Empty(movimientos.Todos);
        Assert.Empty(outbox.Eventos);
    }

    [Fact]
    public async Task Una_devolucion_parcial_solo_reingresa_lo_devuelto()
    {
        // Criterio de HU-014: soporta devolucion parcial.
        var uno = new Existencia("SKU-A", Ubicacion, TipoUbicacion.TIENDA, 4);
        var dos = new Existencia("SKU-B", Ubicacion, TipoUbicacion.TIENDA, 4);
        var (servicio, _, movimientos, _) = Construir(uno, dos);

        await servicio.ReingresarAsync(
            Solicitud(new LineaReingreso("SKU-A", 1, null)), CancellationToken.None);

        Assert.Equal(5, uno.CantidadFisica);
        Assert.Equal(4, dos.CantidadFisica);
        Assert.Single(movimientos.Todos);
    }
}
