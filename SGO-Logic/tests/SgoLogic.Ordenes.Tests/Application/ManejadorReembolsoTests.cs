using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

public sealed class ManejadorReembolsoTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string DevolucionId = "DEV-2026-000001";
    private const string TransactionId = "txn-reembolso-001";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private static Orden CrearOrdenEnDevolucionEnProceso()
    {
        var orden = new Orden(
            OrdenId,
            "000777",
            "CLI-000045",
            Canal.WEB,
            MetodoEntrega.DOMICILIO,
            Moneda.GTQ,
            MedioPago.TARJETA,
            new[] { new LineaOrden("SKU-A", "Laptop gamer", 1, 5000.00m) },
            100.00m);

        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        orden.TransicionarA(EstadoOrden.DESPACHO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.EN_PREPARACION);
        orden.TransicionarA(EstadoOrden.EN_TRANSITO);
        orden.TransicionarA(EstadoOrden.ENTREGADA);
        orden.TransicionarA(EstadoOrden.DEVOLUCION_EN_PROCESO);
        return orden;
    }

    private static Devolucion CrearDevolucionEnEstado(EstadoDevolucion estado)
    {
        var devolucion = new Devolucion(
            DevolucionId,
            OrdenId,
            "CLI-000045",
            TipoDevolucion.DEVOLUCION,
            esParcial: false,
            "UBI-REINGRESO-01",
            5000.00m,
            "PK-AUTH-001",
            "DOC-FISCAL-001",
            "GTQ",
            "admin@sgo.com",
            new[] { new DevolucionLinea("SKU-A", 1, "Defectuoso", null, 5000.00m) });

        if (estado == EstadoDevolucion.REEMBOLSO_APLICADO)
        {
            devolucion.MarcarReembolsoAplicado();
        }
        else if (estado == EstadoDevolucion.REEMBOLSO_FALLIDO)
        {
            devolucion.MarcarReembolsoFallido();
        }

        return devolucion;
    }

    private static (ManejadorEventosPagos Manejador,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad,
        RegistroMensajesProcesadosFalso Registro) Construir()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var registro = new RegistroMensajesProcesadosFalso();
        var servicio = new ServicioConfirmacionOrden(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso(),
            new CatalogoProductosFalso());
        var manejador = new ManejadorEventosPagos(
            registro,
            servicio,
            ordenes,
            unidad,
            NullLogger<ManejadorEventosPagos>.Instance);
        return (manejador, ordenes, outbox, unidad, registro);
    }

    private static EventoPagoRecibido EventoReembolso(
        string tipoEvento,
        string messageId,
        string devolucionId) =>
        new(messageId, TransactionId, tipoEvento, OrdenId, null, null, devolucionId);

    [Fact]
    public async Task Reembolso_aplicado_cierra_a_DEVUELTA()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = CrearOrdenEnDevolucionEnProceso();
        var devolucion = CrearDevolucionEnEstado(EstadoDevolucion.REGISTRADA);
        ordenes.Precargar(orden);
        await ordenes.AgregarDevolucionAsync(devolucion, CancellationToken.None);

        var resultado = await manejador.ManejarAsync(
            EventoReembolso(ReembolsoAplicado.EventType, "EVT-REEMB-001", DevolucionId),
            CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoDevolucion.REEMBOLSO_APLICADO, devolucion.Estado);
        Assert.Equal(EstadoOrden.DEVUELTA, orden.Estado);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Reembolso_fallido_deja_en_DEVOLUCION_EN_PROCESO()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = CrearOrdenEnDevolucionEnProceso();
        var devolucion = CrearDevolucionEnEstado(EstadoDevolucion.REGISTRADA);
        ordenes.Precargar(orden);
        await ordenes.AgregarDevolucionAsync(devolucion, CancellationToken.None);

        var resultado = await manejador.ManejarAsync(
            EventoReembolso(ReembolsoFallido.EventType, "EVT-REEMB-002", DevolucionId),
            CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoDevolucion.REEMBOLSO_FALLIDO, devolucion.Estado);
        Assert.Equal(EstadoOrden.DEVOLUCION_EN_PROCESO, orden.Estado);
        Assert.Equal(1, unidad.Confirmaciones);
    }
}
