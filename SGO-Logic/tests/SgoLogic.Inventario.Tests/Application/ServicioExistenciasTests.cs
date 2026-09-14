using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// HU-016: el ledger de movimientos explica todo cambio de cantidad fisica, y la
/// vista unificada muestra existencias y ubicaciones de todo el sistema en una
/// sola consulta.
/// </summary>
public sealed class ServicioExistenciasTests
{
    private const string Sku = "SKU-LB-00871";
    private const string Ubicacion = "CD-01";

    private static (ServicioExistencias Servicio,
        RepositorioExistenciasFalso Existencias,
        RepositorioMovimientosFalso Movimientos,
        UnidadDeTrabajoFalsa UnidadDeTrabajo) Construir(params Existencia[] precargadas)
    {
        var existencias = new RepositorioExistenciasFalso().Precargar(precargadas);
        var movimientos = new RepositorioMovimientosFalso();
        var unidadDeTrabajo = new UnidadDeTrabajoFalsa();

        return (new ServicioExistencias(existencias, movimientos, unidadDeTrabajo),
            existencias,
            movimientos,
            unidadDeTrabajo);
    }

    private static RegistrarExistenciaRequest Solicitud(
        int cantidadFisica,
        string sku = Sku,
        string ubicacion = Ubicacion,
        TipoUbicacion tipo = TipoUbicacion.CENTRO_DISTRIBUCION) =>
        new(sku, ubicacion, tipo, cantidadFisica);

    [Fact]
    public async Task Alta_de_existencia_asienta_la_carga_inicial_como_entrada()
    {
        var (servicio, _, movimientos, _) = Construir();

        await servicio.RegistrarAsync(Solicitud(10), CancellationToken.None);

        var movimiento = Assert.Single(movimientos.Todos);
        Assert.Equal(TipoMovimiento.ENTRADA, movimiento.Tipo);
        Assert.Equal(MotivoMovimiento.CARGA_INICIAL, movimiento.Motivo);
        Assert.Equal(10, movimiento.Cantidad);
        Assert.Equal(10, movimiento.CantidadFisicaResultante);
    }

    [Fact]
    public async Task Alta_en_cero_no_asienta_movimiento()
    {
        // Un alta sin unidades no mueve nada fisico: crea la fila para poder cargar
        // despues, pero no hay entrada que registrar.
        var (servicio, existencias, movimientos, _) = Construir();

        await servicio.RegistrarAsync(Solicitud(0), CancellationToken.None);

        Assert.Equal(1, existencias.LlamadasAgregar);
        Assert.Empty(movimientos.Todos);
    }

    [Fact]
    public async Task Ajuste_al_alza_asienta_una_entrada_por_el_delta()
    {
        var (servicio, _, movimientos, _) = Construir(
            new Existencia(Sku, Ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, 10));

        await servicio.RegistrarAsync(Solicitud(14), CancellationToken.None);

        var movimiento = Assert.Single(movimientos.Todos);
        Assert.Equal(TipoMovimiento.ENTRADA, movimiento.Tipo);
        Assert.Equal(MotivoMovimiento.AJUSTE_MANUAL, movimiento.Motivo);

        // La cantidad del asiento es el delta, no el saldo nuevo.
        Assert.Equal(4, movimiento.Cantidad);
        Assert.Equal(14, movimiento.CantidadFisicaResultante);
    }

    [Fact]
    public async Task Ajuste_a_la_baja_asienta_una_salida_por_el_delta()
    {
        var (servicio, _, movimientos, _) = Construir(
            new Existencia(Sku, Ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, 10));

        await servicio.RegistrarAsync(Solicitud(3), CancellationToken.None);

        var movimiento = Assert.Single(movimientos.Todos);
        Assert.Equal(TipoMovimiento.SALIDA, movimiento.Tipo);
        Assert.Equal(7, movimiento.Cantidad);
        Assert.Equal(3, movimiento.CantidadFisicaResultante);
    }

    [Fact]
    public async Task Ajuste_al_mismo_valor_no_asienta_movimiento()
    {
        var (servicio, _, movimientos, unidadDeTrabajo) = Construir(
            new Existencia(Sku, Ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, 10));

        await servicio.RegistrarAsync(Solicitud(10), CancellationToken.None);

        Assert.Empty(movimientos.Todos);

        // Aun sin movimiento se confirma: el ajuste subio la version de la existencia.
        Assert.Equal(1, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task El_movimiento_se_confirma_en_la_misma_transaccion_que_la_existencia()
    {
        // Si la transaccion choca, ni el saldo ni su asiento deben quedar: la unica
        // confirmacion es la que cubre a los dos.
        var (servicio, _, _, unidadDeTrabajo) = Construir();
        unidadDeTrabajo.LanzarConflicto = true;

        await Assert.ThrowsAsync<ExcepcionConflictoConcurrencia>(
            () => servicio.RegistrarAsync(Solicitud(10), CancellationToken.None));

        Assert.Equal(0, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task La_vista_unificada_lista_todas_las_ubicaciones_paginando()
    {
        var (servicio, _, _, _) = Construir(
            new Existencia("SKU-A", "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, 10),
            new Existencia("SKU-B", "TDA-07", TipoUbicacion.TIENDA, 4),
            new Existencia("SKU-C", "TDA-12", TipoUbicacion.TIENDA, 9));

        var primera = await servicio.ListarAsync(null, null, 1, 2, CancellationToken.None);

        Assert.Equal(2, primera.Elementos.Count);

        // El total es el conteo completo sin paginar, no el tamano de esta pagina.
        Assert.Equal(3, primera.Total);
        Assert.Equal("SKU-A", primera.Elementos[0].Sku);

        var segunda = await servicio.ListarAsync(null, null, 2, 2, CancellationToken.None);

        Assert.Equal("SKU-C", Assert.Single(segunda.Elementos).Sku);
    }

    [Fact]
    public async Task La_vista_unificada_filtra_por_tipo_de_ubicacion()
    {
        var (servicio, _, _, _) = Construir(
            new Existencia("SKU-A", "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, 10),
            new Existencia("SKU-B", "TDA-07", TipoUbicacion.TIENDA, 4),
            new Existencia("SKU-C", "TDA-12", TipoUbicacion.TIENDA, 9));

        var pagina = await servicio.ListarAsync(null, TipoUbicacion.TIENDA, 1, 50, CancellationToken.None);

        Assert.Equal(2, pagina.Total);
        Assert.All(pagina.Elementos, existencia => Assert.Equal(TipoUbicacion.TIENDA, existencia.TipoUbicacion));
    }

    [Fact]
    public async Task Una_pagina_fuera_de_rango_se_normaliza_al_tamano_por_defecto()
    {
        var (servicio, _, _, _) = Construir(
            new Existencia("SKU-A", "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, 10));

        var pagina = await servicio.ListarAsync(null, null, 0, -5, CancellationToken.None);

        Assert.Equal(1, pagina.Pagina);
        Assert.Equal(50, pagina.Tamano);
        Assert.Single(pagina.Elementos);
    }

    [Fact]
    public async Task El_resumen_de_ubicaciones_agrega_skus_y_descuenta_lo_reservado()
    {
        var comprometida = new Existencia("SKU-A", "TDA-07", TipoUbicacion.TIENDA, 10);
        comprometida.Reservar(4);

        var (servicio, _, _, _) = Construir(
            comprometida,
            new Existencia("SKU-B", "TDA-07", TipoUbicacion.TIENDA, 6),
            new Existencia("SKU-C", "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, 20));

        var ubicaciones = await servicio.ListarUbicacionesAsync(CancellationToken.None);

        Assert.Equal(2, ubicaciones.Count);

        var tienda = ubicaciones.Single(ubicacion => ubicacion.UbicacionId == "TDA-07");
        Assert.Equal(TipoUbicacion.TIENDA, tienda.TipoUbicacion);
        Assert.Equal(2, tienda.CantidadDeSkus);
        Assert.Equal(16, tienda.CantidadTotalFisica);
        Assert.Equal(4, tienda.CantidadTotalReservada);

        // Lo que el operador realmente puede comprometer hoy en esa tienda.
        Assert.Equal(12, tienda.TotalDisponible);
    }

    [Fact]
    public async Task El_historial_de_un_sku_sale_del_mas_reciente_al_mas_antiguo()
    {
        var (servicio, _, _, _) = Construir();

        await servicio.RegistrarAsync(Solicitud(10), CancellationToken.None);
        await servicio.RegistrarAsync(Solicitud(14), CancellationToken.None);
        await servicio.RegistrarAsync(Solicitud(2), CancellationToken.None);

        var historial = await servicio.ListarMovimientosAsync(Sku, null, null, null, CancellationToken.None);

        Assert.Equal(3, historial.Count);
        Assert.Equal(nameof(TipoMovimiento.SALIDA), historial[0].Tipo);
        Assert.Equal(12, historial[0].Cantidad);
        Assert.Equal(2, historial[0].CantidadFisicaResultante);
    }

    [Fact]
    public async Task El_historial_se_acota_por_ubicacion()
    {
        var (servicio, _, _, _) = Construir();

        await servicio.RegistrarAsync(Solicitud(10, ubicacion: "CD-01"), CancellationToken.None);
        await servicio.RegistrarAsync(
            Solicitud(5, ubicacion: "TDA-07", tipo: TipoUbicacion.TIENDA),
            CancellationToken.None);

        var historial = await servicio.ListarMovimientosAsync(Sku, "TDA-07", null, null, CancellationToken.None);

        var movimiento = Assert.Single(historial);
        Assert.Equal("TDA-07", movimiento.UbicacionId);
        Assert.Equal(5, movimiento.Cantidad);
    }

    [Fact]
    public async Task Listar_por_ubicacion_devuelve_solo_lo_de_esa_ubicacion()
    {
        var (servicio, _, _, _) = Construir(
            new Existencia("SKU-A", "TDA-07", TipoUbicacion.TIENDA, 10),
            new Existencia("SKU-B", "TDA-07", TipoUbicacion.TIENDA, 6),
            new Existencia("SKU-C", "CD-01", TipoUbicacion.CENTRO_DISTRIBUCION, 20));

        var existencias = await servicio.ListarPorUbicacionAsync("TDA-07", CancellationToken.None);

        Assert.Equal(2, existencias.Count);
        Assert.All(existencias, existencia => Assert.Equal("TDA-07", existencia.UbicacionId));
    }
}
