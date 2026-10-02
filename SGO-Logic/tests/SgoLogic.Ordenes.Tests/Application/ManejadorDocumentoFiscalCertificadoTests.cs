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
/// Pruebas del cierre del tramo CONFIRMADA_PENDIENTE_FISCAL → CONFIRMADA al
/// consumir DocumentoFiscalCertificado (evento 1 de facturacion-eventos-v1).
/// </summary>
public sealed class ManejadorDocumentoFiscalCertificadoTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string TransactionId = "b2d5e8f1-44a3-4c90-b6e1-2f8a7c3d5e90";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private static Orden CrearOrden() => new(
        OrdenId,
        "000777",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        150.00m,
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

    private static DocumentoFiscalCertificadoData CrearDatosCertificados(string? ordenId = null) => new(
        DocumentoFiscalId: "5e4f3a5f-987a-42ca-888e-063c640ef111",
        OrdenId: ordenId ?? OrdenId,
        TipoDocumento: TipoDocumentoFiscal.FACTURA,
        Serie: "A",
        NumeroDocumento: "00000001",
        NumeroAutorizacion: "89958267-C72E-4760-B3A8-490454540678",
        Folio: "FOL-D19FEE774074",
        Moneda: Moneda.GTQ,
        Total: 1400.00m,
        CertificadaEn: new DateTime(2026, 9, 10, 14, 32, 46, DateTimeKind.Utc));

    private static (ManejadorDocumentoFiscalCertificado Manejador,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad) Construir()
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
            new CatalogoProductosFalso());
        var manejador = new ManejadorDocumentoFiscalCertificado(
            servicio,
            ordenes,
            unidad,
            NullLogger<ManejadorDocumentoFiscalCertificado>.Instance);
        return (manejador, ordenes, outbox, unidad);
    }

    [Fact]
    public async Task Recibir_certificacion_en_CONFIRMADA_PENDIENTE_FISCAL_aplica_CONFIRMADA_y_DESPACHO_SOLICITADO()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Equal(
            new[] { EstadoOrden.CONFIRMADA, EstadoOrden.DESPACHO_SOLICITADO },
            orden.Transiciones.TakeLast(2).Select(t => t.EstadoNuevo).ToArray());
        Assert.Equal("5e4f3a5f-987a-42ca-888e-063c640ef111", orden.DocumentoFiscalId);
        Assert.Equal("A", orden.DocumentoFiscalSerie);
        Assert.Equal("00000001", orden.DocumentoFiscalNumero);
        Assert.Equal("89958267-C72E-4760-B3A8-490454540678", orden.DocumentoFiscalNumeroAutorizacion);
        Assert.Equal("FOL-D19FEE774074", orden.DocumentoFiscalFolio);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Recibir_certificacion_dos_veces_es_idempotente()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);
        var transicionesDespuesDePrimera = orden.Transiciones.Count;
        var eventosDespuesDePrimera = outbox.Eventos.Count;

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Equal(transicionesDespuesDePrimera, orden.Transiciones.Count);
        Assert.Equal(eventosDespuesDePrimera, outbox.Eventos.Count);
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.CONFIRMADA));
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.DESPACHO_SOLICITADO));
        Assert.Single(outbox.PorTipo(OrdenCertificadaFiscalmente.EventType));
        Assert.Single(outbox.PorTipo(DespachoSolicitado.EventType));
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Orden_en_estado_inesperado_no_lanza_y_no_cambia_nada()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrden(); // CREADA
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.CREADA, orden.Estado);
        Assert.Null(orden.DocumentoFiscalId);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Certificacion_emite_evento_12_OrdenCertificadaFiscalmente_en_outbox()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        var estadoCambiado = outbox.PorTipo(OrdenEstadoCambiado.EventType).First();
        var dataEstado = Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, dataEstado.EstadoAnterior);
        Assert.Equal(EstadoOrden.CONFIRMADA, dataEstado.EstadoNuevo);
        Assert.False(dataEstado.EsCompensacion);
        Assert.Equal(PasoSaga.FISCAL, dataEstado.PasoSaga);

        var certificada = Assert.Single(outbox.PorTipo(OrdenCertificadaFiscalmente.EventType));
        Assert.Equal(OrdenCertificadaFiscalmente.RoutingKey, certificada.RoutingKey);
        var dataFiscal = Assert.IsType<OrdenCertificadaFiscalmenteData>(certificada.Data);
        Assert.Equal(OrdenId, dataFiscal.OrdenId);
        Assert.Equal("5e4f3a5f-987a-42ca-888e-063c640ef111", dataFiscal.DocumentoFiscalId);
        Assert.Equal("A", dataFiscal.Serie);
        Assert.Equal("00000001", dataFiscal.NumeroDocumento);
        Assert.Equal("89958267-C72E-4760-B3A8-490454540678", dataFiscal.NumeroAutorizacion);
        Assert.Equal(Moneda.GTQ, dataFiscal.Moneda);
        Assert.Equal(1400.00m, dataFiscal.Total);
    }

    [Fact]
    public async Task Certificacion_emite_DespachoSolicitado_en_la_misma_transaccion_y_en_el_orden_esperado()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        orden.RegistrarReserva("RSV-2026-000042", "CD-01");
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                OrdenEstadoCambiado.EventType,
                OrdenCertificadaFiscalmente.EventType,
                OrdenEstadoCambiado.EventType,
                DespachoSolicitado.EventType
            },
            outbox.Eventos.Select(e => e.EventType).ToArray());

        var cambios = outbox.PorTipo(OrdenEstadoCambiado.EventType)
            .Select(e => Assert.IsType<OrdenEstadoCambiadoData>(e.Data))
            .ToList();
        Assert.Equal(EstadoOrden.CONFIRMADA, cambios[0].EstadoNuevo);
        Assert.Equal(EstadoOrden.CONFIRMADA, cambios[1].EstadoAnterior);
        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, cambios[1].EstadoNuevo);
        Assert.Equal(PasoSaga.DESPACHO, cambios[1].PasoSaga);
        Assert.False(cambios[1].EsCompensacion);

        var despacho = Assert.Single(outbox.PorTipo(DespachoSolicitado.EventType));
        Assert.Equal(DespachoSolicitado.RoutingKey, despacho.RoutingKey);
        Assert.Equal(orden.Id, despacho.AggregateId);
        Assert.Equal(orden.Secuencia, despacho.Sequence);
        var data = Assert.IsType<DespachoSolicitadoData>(despacho.Data);
        Assert.Equal(OrdenId, data.OrdenId);
        Assert.Equal("RSV-2026-000042", data.ReservaId);
        Assert.Equal(MetodoEntrega.DOMICILIO, data.MetodoEntrega);
        Assert.Single(data.Lineas);

        // Certificacion, CONFIRMADA, DESPACHO_SOLICITADO y los cuatro eventos de
        // outbox salen de un solo SaveChanges.
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Una_orden_con_alerta_fiscal_no_se_despacha_mientras_no_llegue_la_certificacion()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        ordenes.Precargar(orden);
        var fallida = new ManejadorCertificacionSatFallida(
            ordenes,
            unidad,
            NullLogger<ManejadorCertificacionSatFallida>.Instance);

        await fallida.ManejarAsync(
            new CertificacionSatFallidaData(
                DocumentoFiscalId: "6f5e4b3c-2a1d-43ca-999f-073d651ef222",
                OrdenId: OrdenId,
                TipoDocumento: TipoDocumentoFiscal.FACTURA,
                Serie: "A",
                NumeroDocumento: "00000002",
                Intentos: 4,
                UltimoCodigo: "SAT_NO_DISPONIBLE",
                UltimoMotivo: "La SAT no respondio dentro del timeout configurado.",
                FallidaEn: new DateTime(2026, 9, 12, 10, 15, 30, DateTimeKind.Utc)),
            CancellationToken.None);

        // La alerta no cambia el estado ni solicita el despacho: la orden queda
        // varada en CONFIRMADA_PENDIENTE_FISCAL hasta el reintento manual.
        Assert.True(orden.AlertaFiscal);
        Assert.Equal(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL, orden.Estado);
        Assert.Empty(outbox.Eventos);

        // Tras el reintento manual la certificacion si llega, y ahi se despacha.
        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Single(outbox.PorTipo(DespachoSolicitado.EventType));
    }

    [Fact]
    public async Task Orden_con_pagoId_real_lo_persiste_para_uso_en_eventos_futuros()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        orden.RegistrarPago("PAG-2026-008877");
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Equal("PAG-2026-008877", orden.PagoId);

        // El evento 12 (OrdenCertificadaFiscalmente) se emitio correctamente.
        var certificada = Assert.Single(outbox.PorTipo(OrdenCertificadaFiscalmente.EventType));
        Assert.Equal(OrdenId, Assert.IsType<OrdenCertificadaFiscalmenteData>(certificada.Data).OrdenId);
    }

    [Fact]
    public async Task Validacion_del_CUI_en_creacion_de_orden()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioConfirmacionOrden(
            ordenes, outbox, unidad,
            new GeneradorCorrelativoOrdenFalso(),
            new PoliticaMediosPago(MediosPago),
            new ClienteCarritoFalso(),
            new CatalogoProductosFalso());

        // CUI valido: solo digitos
        var dto = await servicio.CrearOrdenAsync(
            new CrearOrdenRequest(
                ClienteId: "CLI-000045",
                Canal: Canal.WEB,
                MetodoEntrega: MetodoEntrega.DOMICILIO,
                Moneda: Moneda.GTQ,
                MedioPago: "TARJETA",
                Impuestos: 150.00m,
                Lineas: new[] { new CrearLineaOrdenRequest("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
                ReceptorCui: "2547896320101",
                TokenMedioPago: "tok_pk_9f2a7c41d8e0b3a6c5d4e1f2"),
            CancellationToken.None);

        Assert.NotNull(dto);

        // CUI "CF" es valido
        var dtoCf = await servicio.CrearOrdenAsync(
            new CrearOrdenRequest(
                ClienteId: "CLI-000046",
                Canal: Canal.WEB,
                MetodoEntrega: MetodoEntrega.DOMICILIO,
                Moneda: Moneda.GTQ,
                MedioPago: "TARJETA",
                Impuestos: 100.00m,
                Lineas: new[] { new CrearLineaOrdenRequest("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
                ReceptorCui: "CF",
                TokenMedioPago: "tok_pk_9f2a7c41d8e0b3a6c5d4e1f2"),
            CancellationToken.None);

        Assert.NotNull(dtoCf);

        // CUI invalido: letras y numeros mezclados
        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearOrdenAsync(
            new CrearOrdenRequest(
                ClienteId: "CLI-000047",
                Canal: Canal.WEB,
                MetodoEntrega: MetodoEntrega.DOMICILIO,
                Moneda: Moneda.GTQ,
                MedioPago: "TARJETA",
                Impuestos: 100.00m,
                Lineas: new[] { new CrearLineaOrdenRequest("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
                ReceptorCui: "ABC123",
                TokenMedioPago: "tok_pk_9f2a7c41d8e0b3a6c5d4e1f2"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Certificacion_en_orden_YA_CONFIRMADA_no_hace_nada()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = OrdenEnConfirmadaPendienteFiscal();
        // La pasamos a CONFIRMADA manualmente (simula que ya se certifico antes)
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.CONFIRMADA, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Certificacion_en_orden_CANCELADA_no_hace_nada()
    {
        var (manejador, ordenes, outbox, unidad) = Construir();
        var orden = CrearOrden();
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(CrearDatosCertificados(), CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }
}
