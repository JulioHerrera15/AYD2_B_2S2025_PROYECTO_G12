using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

public sealed class ServicioDevolucionTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string TransactionId = "txn-dev-test-001";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private static Orden CrearOrdenConDosLineas() => new(
        OrdenId,
        "000777",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[]
        {
            new LineaOrden("SKU-A", "Laptop gamer", 2, 5000.00m),
            new LineaOrden("SKU-B", "Mouse inalambrico", 1, 150.00m)
        },
        200.00m);

    private static Orden CrearOrdenConUnaLinea() => new(
        OrdenId,
        "000777",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-A", "Laptop gamer", 1, 5000.00m) },
        100.00m);

    private static Orden CrearOrdenEntregadaConDosLineas()
    {
        var orden = CrearOrdenConDosLineas();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-000777", "CD-01");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        orden.TransicionarA(EstadoOrden.DESPACHO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.EN_PREPARACION);
        orden.TransicionarA(EstadoOrden.EN_TRANSITO);
        orden.TransicionarA(EstadoOrden.ENTREGADA);
        return orden;
    }

    private static Orden CrearOrdenEntregadaConUnaLinea()
    {
        var orden = CrearOrdenConUnaLinea();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-000778", "CD-01");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        orden.TransicionarA(EstadoOrden.DESPACHO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.EN_PREPARACION);
        orden.TransicionarA(EstadoOrden.EN_TRANSITO);
        orden.TransicionarA(EstadoOrden.ENTREGADA);
        return orden;
    }

    private static (ServicioDevolucion Servicio,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad) Construir()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioDevolucion(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoDevolucionFalso(),
            new ServicioConfirmacionOrden(
                ordenes,
                outbox,
                unidad,
                new GeneradorCorrelativoOrdenFalso(),
                new PoliticaMediosPago(MediosPago)));
        return (servicio, ordenes, outbox, unidad);
    }

    private static SolicitudDevolucion Solicitud(
        string ordenId = OrdenId,
        params LineaDevolucionSolicitud[] lineas) => new(
        ordenId,
        TipoDevolucion.DEVOLUCION,
        "UBI-REINGRESO-01",
        "admin@sgo.com",
        lineas);

    [Fact]
    public async Task Devolucion_parcial_valida_se_registra_y_transiciona()
    {
        var (servicio, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrdenEntregadaConDosLineas();
        ordenes.Precargar(orden);

        var solicitud = Solicitud(
            lineas: new LineaDevolucionSolicitud("SKU-A", 1, "Producto defectuoso"));

        var dto = await servicio.RegistrarDevolucionAsync(solicitud, CancellationToken.None);

        Assert.Matches(@"^DEV-\d{4}-\d{6}$", dto.Id);
        Assert.Equal(OrdenId, dto.OrdenOriginalId);
        Assert.Equal(TipoDevolucion.DEVOLUCION, dto.Tipo);
        Assert.True(dto.EsParcial);
        Assert.Equal(5000.00m, dto.MontoReembolso);
        Assert.Equal("GTQ", dto.Moneda);
        Assert.Equal(EstadoDevolucion.REGISTRADA, dto.Estado);
        Assert.Single(dto.Lineas);
        Assert.Equal("SKU-A", dto.Lineas[0].Sku);
        Assert.Equal(1, dto.Lineas[0].Cantidad);
        Assert.Equal(5000.00m, dto.Lineas[0].PrecioUnitario);
        Assert.Equal(5000.00m, dto.Lineas[0].Subtotal);

        Assert.Equal(EstadoOrden.DEVOLUCION_EN_PROCESO, orden.Estado);

        var eventoDevolucion = Assert.Single(outbox.PorTipo(DevolucionRegistrada.EventType));
        Assert.Equal(DevolucionRegistrada.AggregateType, eventoDevolucion.AggregateType);
        Assert.Equal(dto.Id, eventoDevolucion.AggregateId);
        Assert.Equal(DevolucionRegistrada.RoutingKey, eventoDevolucion.RoutingKey);
        var data = Assert.IsType<DevolucionRegistradaData>(eventoDevolucion.Data);
        Assert.Equal(dto.Id, data.DevolucionId);
        Assert.Equal(OrdenId, data.OrdenOriginalId);
        Assert.True(data.EsParcial);
        Assert.Equal("5000.00", data.MontoReembolso);
        Assert.Single(data.Lineas);
        Assert.Equal("RSV-000777", data.Lineas[0].ReservaId);
        Assert.Equal("CD-01", data.Lineas[0].UbicacionOrigenReservaId);

        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Devolucion_que_excede_lo_comprado_es_rechazada()
    {
        var (servicio, ordenes, _, _) = Construir();
        var orden = CrearOrdenEntregadaConUnaLinea();
        ordenes.Precargar(orden);

        var solicitud = Solicitud(
            lineas: new LineaDevolucionSolicitud("SKU-A", 2, "No me gusta"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => servicio.RegistrarDevolucionAsync(solicitud, CancellationToken.None));

        Assert.Contains("excede la cantidad disponible", ex.Message);
        Assert.Contains("SKU-A", ex.Message);
    }

    [Fact]
    public async Task Devolucion_sobre_orden_no_entregada_es_rechazada()
    {
        var (servicio, ordenes, _, _) = Construir();
        var orden = CrearOrdenConUnaLinea();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        ordenes.Precargar(orden);

        var solicitud = Solicitud(
            lineas: new LineaDevolucionSolicitud("SKU-A", 1, "Cambio de opinion"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.RegistrarDevolucionAsync(solicitud, CancellationToken.None));

        Assert.Contains("Solo se pueden registrar devoluciones sobre ordenes en estado ENTREGADA", ex.Message);
    }

    [Fact]
    public async Task Devolucion_sobre_cantidad_ya_devuelta_es_rechazada()
    {
        var (servicio, ordenes, _, _) = Construir();
        var orden = CrearOrdenEntregadaConUnaLinea();
        ordenes.Precargar(orden);

        var primeraSolicitud = Solicitud(
            lineas: new LineaDevolucionSolicitud("SKU-A", 1, "Defectuoso"));
        await servicio.RegistrarDevolucionAsync(primeraSolicitud, CancellationToken.None);

        Assert.Equal(EstadoOrden.DEVOLUCION_EN_PROCESO, orden.Estado);

        var segundaSolicitud = Solicitud(
            lineas: new LineaDevolucionSolicitud("SKU-A", 1, "Otro defecto"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.RegistrarDevolucionAsync(segundaSolicitud, CancellationToken.None));

        Assert.Contains("Solo se pueden registrar devoluciones sobre ordenes en estado ENTREGADA", ex.Message);
    }

    [Fact]
    public async Task Devolucion_total_no_es_parcial()
    {
        var (servicio, ordenes, _, _) = Construir();
        var orden = CrearOrdenEntregadaConUnaLinea();
        ordenes.Precargar(orden);

        var solicitud = Solicitud(
            lineas: new LineaDevolucionSolicitud("SKU-A", 1, "Ya no lo quiero"));

        var dto = await servicio.RegistrarDevolucionAsync(solicitud, CancellationToken.None);

        Assert.False(dto.EsParcial);
        Assert.Equal(5000.00m, dto.MontoReembolso);
    }
}
