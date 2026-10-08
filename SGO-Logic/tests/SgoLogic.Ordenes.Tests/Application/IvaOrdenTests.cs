using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes.Eventos;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Los precios del catalogo ya incluyen IVA: Ordenes calcula total e IVA en el
/// servidor desde las lineas e ignora el campo Impuestos del request.
/// </summary>
public sealed class IvaOrdenTests
{
    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA }
    };

    private static (ServicioConfirmacionOrden Servicio, OutboxWriterFalso Outbox) Construir(CatalogoProductosFalso catalogo, decimal tasa = 0.12m)
    {
        var outbox = new OutboxWriterFalso();
        var servicio = new ServicioConfirmacionOrden(
            new RepositorioOrdenesFalso(),
            outbox,
            new UnidadDeTrabajoFalsa(),
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso(),
            catalogo,
            Options.Create(new FiscalOptions { TasaIva = tasa }));
        return (servicio, outbox);
    }

    private static CrearOrdenRequest Solicitud(decimal impuestos, params CrearLineaOrdenRequest[] lineas) => new(
        ClienteId: "cliente1@sgo.local",
        Canal: Canal.WEB,
        MetodoEntrega: MetodoEntrega.DOMICILIO,
        Moneda: Moneda.GTQ,
        MedioPago: "TRANSFERENCIA",
        Impuestos: impuestos,
        Lineas: lineas);

    [Fact]
    public async Task Un_carrito_de_2000_da_una_orden_de_2000_con_IVA_incluido_de_214_29()
    {
        var catalogo = new CatalogoProductosFalso().ConPrecio("SKU-A", 1000.00m);
        var (servicio, outbox) = Construir(catalogo);

        var orden = await servicio.CrearOrdenAsync(
            Solicitud(214.29m, new CrearLineaOrdenRequest("SKU-A", "Producto A", 2, 1000.00m)), CancellationToken.None);

        Assert.Equal(2000.00m, orden.Total);
        Assert.Equal(214.29m, orden.Impuestos);
        Assert.Equal(1785.71m, orden.Subtotal);

        var creada = Assert.IsType<OrdenCreadaData>(Assert.Single(outbox.PorTipo(OrdenCreada.EventType)).Data);
        Assert.Equal(2000.00m, creada.Total);
        Assert.Equal(214.29m, creada.Impuestos);
        Assert.Equal(1785.71m, creada.Subtotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(214.29)]
    [InlineData(9999.99)]
    public async Task El_campo_Impuestos_del_request_se_ignora(double impuestosEnviados)
    {
        var catalogo = new CatalogoProductosFalso().ConPrecio("SKU-A", 1000.00m);
        var (servicio, _) = Construir(catalogo);

        var orden = await servicio.CrearOrdenAsync(
            Solicitud((decimal)impuestosEnviados, new CrearLineaOrdenRequest("SKU-A", "Producto A", 2, 1000.00m)), CancellationToken.None);

        Assert.Equal(2000.00m, orden.Total);
        Assert.Equal(214.29m, orden.Impuestos);
    }

    [Fact]
    public async Task La_tasa_sale_de_la_configuracion()
    {
        var catalogo = new CatalogoProductosFalso().ConPrecio("SKU-A", 1000.00m);
        var (servicio, _) = Construir(catalogo, tasa: 0.05m);

        var orden = await servicio.CrearOrdenAsync(
            Solicitud(0m, new CrearLineaOrdenRequest("SKU-A", "Producto A", 2, 1000.00m)), CancellationToken.None);

        // 2000 - 2000 / 1.05 = 95.238... -> 95.24
        Assert.Equal(95.24m, orden.Impuestos);
        Assert.Equal(2000.00m, orden.Total);
    }

    [Fact]
    public async Task El_IVA_por_linea_de_la_factura_suma_exactamente_el_IVA_de_la_orden()
    {
        var catalogo = new CatalogoProductosFalso()
            .ConPrecio("SKU-A", 3299.00m)
            .ConPrecio("SKU-B", 1899.00m);
        var (servicio, _) = Construir(catalogo);

        var dto = await servicio.CrearOrdenAsync(
            Solicitud(0m,
                new CrearLineaOrdenRequest("SKU-A", "Barra de sonido", 1, 3299.00m),
                new CrearLineaOrdenRequest("SKU-B", "Microondas", 2, 1899.00m)),
            CancellationToken.None);

        Assert.Equal(7097.00m, dto.Total);
        Assert.Equal(760.39m, dto.Impuestos);

        var orden = new SgoLogic.Ordenes.Api.Domain.Orden(
            dto.Id, dto.NumeroOrden, dto.ClienteId, Canal.WEB, MetodoEntrega.DOMICILIO, Moneda.GTQ, MedioPago.TRANSFERENCIA,
            new[]
            {
                new SgoLogic.Ordenes.Api.Domain.LineaOrden("SKU-A", "Barra de sonido", 1, 3299.00m),
                new SgoLogic.Ordenes.Api.Domain.LineaOrden("SKU-B", "Microondas", 2, 1899.00m)
            },
            dto.Impuestos);

        var lineas = MapaEventosSagaOrden.LineasFiscales(orden);
        Assert.Equal(760.39m, lineas.Sum(linea => linea.Impuesto));
        Assert.Equal(353.46m, lineas[0].Impuesto);
        Assert.Equal(406.93m, lineas[1].Impuesto);
    }

    [Fact]
    public void Calculadora_redondea_a_dos_decimales()
    {
        Assert.Equal(214.29m, CalculadoraIva.IvaIncluido(2000.00m, 0.12m));
        Assert.Equal(589.18m, CalculadoraIva.IvaIncluido(5499.00m, 0.12m));
        Assert.Equal(0m, CalculadoraIva.IvaIncluido(0m, 0.12m));
    }
}
