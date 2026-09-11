using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Pruebas del manejo de CertificacionSatFallida (evento 2 de facturacion-eventos-v1)
/// y del reintento manual de certificacion fiscal.
/// </summary>
public sealed class ManejadorCertificacionSatFallidaTests
{
    private const string OrdenId = "ORD-2026-000888";
    private const string TransactionId = "c3e6f9a2-55b4-4d91-c7f2-3a9b8d4e6f01";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private static Orden CrearOrden() => new(
        OrdenId,
        "000888",
        "CLI-000050",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-LB-00872", "Laptop Gamer", 1, 8500.00m) },
        1020.00m,
        receptorCui: "2547896320101");

    private static Orden OrdenEnConfirmadaPendienteFiscal()
    {
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        return orden;
    }

    private static CertificacionSatFallidaData CrearDatosFallida(string? ordenId = null) => new(
        DocumentoFiscalId: "6f5e4b3c-2a1d-43ca-999f-073d651ef222",
        OrdenId: ordenId ?? OrdenId,
        TipoDocumento: TipoDocumentoFiscal.FACTURA,
        Serie: "A",
        NumeroDocumento: "00000002",
        Intentos: 4,
        UltimoCodigo: "SAT_NO_DISPONIBLE",
        UltimoMotivo: "La SAT no respondio dentro del timeout configurado.",
        FallidaEn: new DateTime(2026, 9, 12, 10, 15, 30, DateTimeKind.Utc));

    private static (ManejadorCertificacionSatFallida Manejador,
        RepositorioOrdenesFalso Ordenes,
        UnidadDeTrabajoFalsa Unidad) ConstruirHandler()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var manejador = new ManejadorCertificacionSatFallida(
            ordenes,
            unidad,
            NullLogger<ManejadorCertificacionSatFallida>.Instance);
        return (manejador, ordenes, unidad);
    }

    private static (ServicioConfirmacionOrden Servicio,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad) ConstruirServicio()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioConfirmacionOrden(
            ordenes,
            outbox,
            unidad,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago));
        return (servicio, ordenes, outbox, unidad);
    }

    [Fact]
    public async Task Recibir_fallida_en_CONFIRMADA_PENDIENTE_FISCAL_levanta_alerta()
    {
        var (manejador, ordenes, unidad) = ConstruirHandler();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosFallida(), CancellationToken.None);

        Assert.True(orden.AlertaFiscal);
        Assert.NotNull(orden.AlertaFiscalEn);
        Assert.Contains("SAT_NO_DISPONIBLE", orden.AlertaFiscalMotivo!);
        Assert.Contains("4 intentos agotados", orden.AlertaFiscalMotivo!);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, orden.Estado);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Reintento_manual_exitoso_limpia_bandera_y_encola_evento()
    {
        var (servicio, ordenes, outbox, unidad) = ConstruirServicio();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        orden.LevantarAlertaFiscal("Certificacion SAT fallida: 4 intentos", DateTime.UtcNow);
        ordenes.Precargar(orden);

        var resultado = await servicio.ReintentarCertificacionAsync(OrdenId, CancellationToken.None);

        Assert.False(orden.AlertaFiscal);
        Assert.Null(orden.AlertaFiscalMotivo);
        Assert.Null(orden.AlertaFiscalEn);
        Assert.False(resultado.SinCambios);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, orden.Estado);
        Assert.Equal(1, unidad.Confirmaciones);

        var solicitado = Assert.Single(outbox.PorTipo(CertificacionFiscalSolicitada.EventType));
        Assert.Equal(CertificacionFiscalSolicitada.RoutingKey, solicitado.RoutingKey);
        var data = Assert.IsType<CertificacionFiscalSolicitadaData>(solicitado.Data);
        Assert.Equal(OrdenId, data.OrdenId);
        Assert.Equal(TipoDocumentoFiscal.FACTURA, data.TipoDocumento);
        Assert.Equal(orden.Moneda, data.Moneda);
        Assert.Equal(orden.Total, data.Total);
    }

    [Fact]
    public async Task Reintento_rechazado_409_cuando_no_hay_alerta()
    {
        var (servicio, ordenes, outbox, unidad) = ConstruirServicio();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        ordenes.Precargar(orden);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.ReintentarCertificacionAsync(OrdenId, CancellationToken.None));

        Assert.Contains("no tiene alerta fiscal levantada", ex.Message);
        Assert.False(orden.AlertaFiscal);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Llegada_fuera_de_orden_cuando_ya_esta_confirmada_se_ignora()
    {
        var (manejador, ordenes, unidad) = ConstruirHandler();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosFallida(), CancellationToken.None);

        Assert.False(orden.AlertaFiscal);
        Assert.Equal(EstadoOrden.CONFIRMADA, orden.Estado);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Reintento_rechazado_409_cuando_estado_no_es_CONFIRMADA_PENDIENTE_FISCAL()
    {
        var (servicio, ordenes, outbox, unidad) = ConstruirServicio();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        orden.LevantarAlertaFiscal("test", DateTime.UtcNow);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        ordenes.Precargar(orden);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.ReintentarCertificacionAsync(OrdenId, CancellationToken.None));

        Assert.Contains("CONFIRMADA", ex.Message);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Reintento_rechazado_409_cuando_la_orden_no_existe()
    {
        var (servicio, ordenes, outbox, unidad) = ConstruirServicio();

        await Assert.ThrowsAsync<ExcepcionOrdenNoEncontrada>(
            () => servicio.ReintentarCertificacionAsync("ORD-2026-999999", CancellationToken.None));

        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }
}
