using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Cubre el ciclo completo de una devolucion parcial (HU-014): la orden entregada
/// puede recibir mas de una devolucion sobre unidades distintas de la misma linea,
/// volviendo a ENTREGADA entre reembolsos parciales y cerrando en DEVUELTA solo
/// cuando, en forma acumulada, ya no queda nada pendiente de devolver.
/// </summary>
public sealed class DevolucionParcialFlujoTests
{
    private const string OrdenId = "ORD-2026-000888";
    private const string TransactionId = "txn-dev-parcial-001";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private static Orden CrearOrdenEntregadaConDosUnidades()
    {
        var orden = new Orden(
            OrdenId,
            "000888",
            "CLI-000099",
            Canal.WEB,
            MetodoEntrega.DOMICILIO,
            Moneda.GTQ,
            MedioPago.TARJETA,
            new[] { new LineaOrden("SKU-A", "Laptop gamer", 2, 5000.00m) },
            100.00m);

        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-000888", "CD-01");
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

    private static (ServicioDevolucion ServicioDevolucion,
        ManejadorEventosPagos ManejadorReembolso,
        RepositorioOrdenesFalso Ordenes)
        Construir()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var registro = new RegistroMensajesProcesadosFalso();
        var politicaMediosPago = new PoliticaMediosPago(MediosPago);
        var servicioConfirmacion = new ServicioConfirmacionOrden(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoOrdenFalso(),
            politicaMediosPago,
            new ClienteCarritoFalso());

        var servicioDevolucion = new ServicioDevolucion(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoDevolucionFalso(),
            servicioConfirmacion);

        var manejadorReembolso = new ManejadorEventosPagos(
            registro,
            servicioConfirmacion,
            ordenes,
            unidad,
            NullLogger<ManejadorEventosPagos>.Instance);

        return (servicioDevolucion, manejadorReembolso, ordenes);
    }

    private static SolicitudDevolucion Solicitud(int cantidad) => new(
        OrdenId,
        TipoDevolucion.DEVOLUCION,
        "UBI-REINGRESO-01",
        "admin@sgo.com",
        new[] { new LineaDevolucionSolicitud("SKU-A", cantidad, "Defectuoso") });

    private static EventoPagoRecibido EventoReembolsoAplicado(string messageId, string devolucionId) =>
        new(messageId, TransactionId, ReembolsoAplicado.EventType, OrdenId, null, null, devolucionId);

    [Fact]
    public async Task Dos_devoluciones_parciales_sucesivas_cierran_la_orden_en_DEVUELTA()
    {
        var (servicioDevolucion, manejadorReembolso, ordenes) = Construir();
        var orden = CrearOrdenEntregadaConDosUnidades();
        ordenes.Precargar(orden);

        // Primera devolucion: 1 de las 2 unidades. Es parcial: la orden vuelve a
        // ENTREGADA cuando el reembolso se aplica, no se cierra en DEVUELTA.
        var primeraDevolucion = await servicioDevolucion.RegistrarDevolucionAsync(Solicitud(1), CancellationToken.None);
        Assert.True(primeraDevolucion.EsParcial);
        Assert.Equal(EstadoOrden.DEVOLUCION_EN_PROCESO, orden.Estado);

        var resultadoPrimerReembolso = await manejadorReembolso.ManejarAsync(
            EventoReembolsoAplicado("EVT-REEMB-PARCIAL-001", primeraDevolucion.Id),
            CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultadoPrimerReembolso);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);

        // Segunda devolucion: la unidad restante. Sumada a la primera cubre toda la
        // linea, asi que ya no es parcial y el reembolso cierra la orden.
        var segundaDevolucion = await servicioDevolucion.RegistrarDevolucionAsync(Solicitud(1), CancellationToken.None);
        Assert.False(segundaDevolucion.EsParcial);
        Assert.Equal(EstadoOrden.DEVOLUCION_EN_PROCESO, orden.Estado);

        var resultadoSegundoReembolso = await manejadorReembolso.ManejarAsync(
            EventoReembolsoAplicado("EVT-REEMB-PARCIAL-002", segundaDevolucion.Id),
            CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultadoSegundoReembolso);
        Assert.Equal(EstadoOrden.DEVUELTA, orden.Estado);
    }

    [Fact]
    public async Task Segunda_devolucion_que_excede_las_unidades_restantes_es_rechazada()
    {
        var (servicioDevolucion, manejadorReembolso, ordenes) = Construir();
        var orden = CrearOrdenEntregadaConDosUnidades();
        ordenes.Precargar(orden);

        var primeraDevolucion = await servicioDevolucion.RegistrarDevolucionAsync(Solicitud(1), CancellationToken.None);
        await manejadorReembolso.ManejarAsync(
            EventoReembolsoAplicado("EVT-REEMB-PARCIAL-003", primeraDevolucion.Id),
            CancellationToken.None);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);

        // Solo queda 1 unidad disponible (2 compradas - 1 ya devuelta); pedir 2 debe
        // rechazarse aunque la orden este de nuevo en ENTREGADA.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => servicioDevolucion.RegistrarDevolucionAsync(Solicitud(2), CancellationToken.None));

        Assert.Contains("excede la cantidad disponible", ex.Message);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);
    }
}
