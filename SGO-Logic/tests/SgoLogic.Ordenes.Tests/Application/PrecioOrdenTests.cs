using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// B1: el precio de cada linea se valida contra el catalogo de Inventario antes de
/// crear la orden. Un precio alterado se rechaza (el endpoint lo traduce a 422 con el
/// SKU) sin persistir ni encolar nada; un precio correcto pasa.
/// </summary>
public sealed class PrecioOrdenTests
{
    private const string Sku = "SKU-LB-00871";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA }
    };

    private static CrearOrdenRequest Solicitud(decimal precioUnitario) => new(
        ClienteId: "cliente1@sgo.local",
        Canal: Canal.WEB,
        MetodoEntrega: MetodoEntrega.DOMICILIO,
        Moneda: Moneda.GTQ,
        MedioPago: "TRANSFERENCIA",
        Impuestos: 133.93m,
        Lineas: new[] { new CrearLineaOrdenRequest(Sku, "Refrigeradora 12 pies", 1, precioUnitario) });

    private static (ServicioConfirmacionOrden Servicio, RepositorioOrdenesFalso Ordenes, OutboxWriterFalso Outbox, UnidadDeTrabajoFalsa Unidad)
        Construir(CatalogoProductosFalso catalogo)
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioConfirmacionOrden(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso(),
            catalogo);
        return (servicio, ordenes, outbox, unidad);
    }

    [Fact]
    public async Task Precio_alterado_rechaza_la_orden_con_el_sku_afectado_sin_persistir_nada()
    {
        var catalogo = new CatalogoProductosFalso().ConPrecio(Sku, 1250.00m);
        var (servicio, ordenes, outbox, unidad) = Construir(catalogo);

        var ex = await Assert.ThrowsAsync<ExcepcionPrecioNoValido>(
            () => servicio.CrearOrdenAsync(Solicitud(precioUnitario: 1.00m), CancellationToken.None));

        Assert.Equal(Sku, ex.Sku);
        Assert.Equal(1250.00m, ex.PrecioVigente);
        Assert.Equal(0, ordenes.LlamadasAgregar);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Precio_correcto_crea_la_orden()
    {
        var catalogo = new CatalogoProductosFalso().ConPrecio(Sku, 1250.00m);
        var (servicio, ordenes, _, unidad) = Construir(catalogo);

        var orden = await servicio.CrearOrdenAsync(Solicitud(precioUnitario: 1250.00m), CancellationToken.None);

        Assert.Equal(EstadoOrden.CREADA, orden.Estado);
        Assert.Equal(1, ordenes.LlamadasAgregar);
        Assert.Equal(1, unidad.Confirmaciones);
        Assert.Contains(Sku, catalogo.Consultas);
    }

    [Fact]
    public async Task Sku_que_no_existe_en_el_catalogo_rechaza_la_orden()
    {
        var (servicio, ordenes, _, _) = Construir(new CatalogoProductosFalso());

        var ex = await Assert.ThrowsAsync<ExcepcionPrecioNoValido>(() => servicio.CrearOrdenAsync(
            Solicitud(1250.00m) with { Lineas = new[] { new CrearLineaOrdenRequest("SKU-INEXISTENTE", "X", 1, 10.00m) } },
            CancellationToken.None));

        Assert.Equal("SKU-INEXISTENTE", ex.Sku);
        Assert.Null(ex.PrecioVigente);
        Assert.Equal(0, ordenes.LlamadasAgregar);
    }

    [Fact]
    public async Task Inventario_caido_impide_crear_la_orden()
    {
        var (servicio, ordenes, _, _) = Construir(new CatalogoProductosFalso { Caido = true });

        await Assert.ThrowsAsync<ExcepcionCatalogoNoDisponible>(
            () => servicio.CrearOrdenAsync(Solicitud(1250.00m), CancellationToken.None));

        Assert.Equal(0, ordenes.LlamadasAgregar);
    }
}
