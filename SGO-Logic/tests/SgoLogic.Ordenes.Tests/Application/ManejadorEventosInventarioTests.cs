using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

public sealed class ManejadorEventosInventarioTests
{
    private const string OrdenId = "ORD-2026-000321";
    private const string ReservaId = "RSV-2026-000321";

    private static Orden CrearOrden() => new(
        OrdenId,
        "000321",
        "cliente1@sgo.local",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("PROD-002", "Smart TV", 1, 5499.00m) },
        589.18m);

    private static (ManejadorEventosInventario Manejador,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        RegistroMensajesProcesadosFalso Registro) Construir(Orden orden)
    {
        var ordenes = new RepositorioOrdenesFalso();
        ordenes.Precargar(orden);
        var outbox = new OutboxWriterFalso();
        var registro = new RegistroMensajesProcesadosFalso();

        var manejador = new ManejadorEventosInventario(
            registro,
            ordenes,
            outbox,
            new UnidadDeTrabajoFalsa(),
            NullLogger<ManejadorEventosInventario>.Instance);

        return (manejador, ordenes, outbox, registro);
    }

    private static EventoInventarioRecibido InventarioReservadoEvento() => new(
        "msg-reservado-1",
        "trx-1",
        InventarioReservado.EventType,
        OrdenId,
        new InventarioReservadoData(
            ReservaId,
            OrdenId,
            "CD-CENTRAL",
            TipoUbicacion.CENTRO_DISTRIBUCION,
            900,
            new[] { new ReservaConfirmadaLineaData("PROD-002", 1) },
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(15)));

    [Fact]
    public async Task InventarioReservado_tardio_sobre_orden_cancelada_pide_liberar_sin_mover_la_orden()
    {
        // El cliente cancelo en RESERVA_SOLICITADA, antes de conocer el ReservaId.
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.CANCELADA, esCompensacion: true);
        var transicionesAntes = orden.Transiciones.Count;

        var (manejador, _, outbox, registro) = Construir(orden);

        var resultado = await manejador.ManejarAsync(InventarioReservadoEvento(), CancellationToken.None);

        Assert.Equal(ResultadoEventoInventario.Aplicado, resultado);
        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Equal(ReservaId, orden.ReservaId);

        var evento = Assert.Single(outbox.Eventos);
        Assert.Equal(LiberacionReservaSolicitada.EventType, evento.EventType);
        var datos = Assert.IsType<LiberacionReservaSolicitadaData>(evento.Data);
        Assert.Equal(OrdenId, datos.OrdenId);
        Assert.Equal(ReservaId, datos.ReservaId);
        Assert.Equal(MotivoLiberacionReserva.CANCELACION_CLIENTE, datos.Motivo);

        Assert.Single(registro.Registrados);
    }

    [Fact]
    public async Task ReservaInventarioRechazada_deja_un_motivo_legible_para_el_cliente()
    {
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        var (manejador, _, outbox, _) = Construir(orden);

        var evento = new EventoInventarioRecibido(
            "msg-rechazada-1",
            "trx-2",
            ReservaInventarioRechazada.EventType,
            OrdenId,
            new ReservaInventarioRechazadaData(
                OrdenId,
                MotivoRechazoReserva.STOCK_INSUFICIENTE,
                new[] { new ReservaRechazadaLineaData("PROD-002", 1, 0) },
                DateTime.UtcNow));

        await manejador.ManejarAsync(evento, CancellationToken.None);

        Assert.Equal(EstadoOrden.RECHAZADA, orden.Estado);
        Assert.Equal("El producto ya fue reservado previamente", orden.Transiciones.Last().Motivo);

        var cambio = Assert.IsType<OrdenEstadoCambiadoData>(
            Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType)).Data);
        Assert.Equal("El producto ya fue reservado previamente", cambio.Motivo);

        var cancelada = Assert.IsType<OrdenCanceladaData>(
            Assert.Single(outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.Equal("El producto ya fue reservado previamente", cancelada.Detalle);
    }
}
