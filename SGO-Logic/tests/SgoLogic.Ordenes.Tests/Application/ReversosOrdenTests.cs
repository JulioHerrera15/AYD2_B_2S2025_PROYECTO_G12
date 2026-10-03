using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Reversos de dinero en Ordenes (bloque 4): medio de pago en DevolucionRegistrada,
/// cierre de la devolucion a credito con DevolucionCreditoAplicada, estado del reembolso
/// por cancelacion y la nota de credito certificada sobre una orden cancelada.
/// </summary>
public sealed class ReversosOrdenTests
{
    private const string OrdenId = "ORD-2026-000888";
    private const string Cliente = "cliente1@sgo.local";
    private const string DevolucionId = "DEV-2026-000050";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.CREDITO }
    };

    private readonly RepositorioOrdenesFalso _ordenes = new();
    private readonly OutboxWriterFalso _outbox = new();
    private readonly UnidadDeTrabajoFalsa _unidad = new();
    private readonly RegistroMensajesProcesadosFalso _registro = new();

    private ServicioConfirmacionOrden Servicio() => new(
        _ordenes, _outbox, _unidad, new GeneradorCorrelativoOrdenFalso(), new PoliticaMediosPago(MediosPago),
        new ClienteCarritoFalso(), new CatalogoProductosFalso());

    private ManejadorEventosPagos ManejadorPagos() =>
        new(_registro, Servicio(), _ordenes, _unidad, NullLogger<ManejadorEventosPagos>.Instance);

    private ManejadorEventosCredito ManejadorCredito() =>
        new(_registro, Servicio(), _ordenes, _unidad, NullLogger<ManejadorEventosCredito>.Instance);

    private ServicioDevolucion Devoluciones() =>
        new(_ordenes, _outbox, _unidad, new GeneradorCorrelativoDevolucionFalso(), Servicio());

    private static Orden NuevaOrden(MedioPago medio) => new(
        OrdenId, "000888", Cliente, Canal.WEB, MetodoEntrega.DOMICILIO, Moneda.GTQ, medio,
        new[] { new LineaOrden("SKU-A", "Laptop gamer", 2, 2500.00m) }, 535.71m);

    /// <summary>Orden confirmada por la ruta que corresponde a su medio de pago.</summary>
    private static Orden OrdenConfirmada(MedioPago medio)
    {
        var orden = NuevaOrden(medio);
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.RegistrarReserva("RSV-000888", "CD-01");
        orden.TransicionarA(EstadoOrden.INVENTARIO_RESERVADO);
        if (medio == MedioPago.CREDITO)
        {
            orden.TransicionarA(EstadoOrden.EVALUACION_CREDITO_SOLICITADA);
            orden.TransicionarA(EstadoOrden.CREDITO_APROBADO);
        }
        else
        {
            orden.TransicionarA(EstadoOrden.COBRO_SOLICITADO);
            orden.TransicionarA(EstadoOrden.PAGO_APLICADO);
        }

        orden.TransicionarA(EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL);
        orden.TransicionarA(EstadoOrden.CONFIRMADA);
        return orden;
    }

    private static Orden OrdenEntregada(MedioPago medio)
    {
        var orden = OrdenConfirmada(medio);
        orden.TransicionarA(EstadoOrden.DESPACHO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.EN_PREPARACION);
        orden.TransicionarA(EstadoOrden.EN_TRANSITO);
        orden.TransicionarA(EstadoOrden.ENTREGADA);
        return orden;
    }

    private async Task<(Orden Orden, Devolucion Devolucion)> DevolucionEnProcesoAsync(bool esParcial)
    {
        var orden = OrdenEntregada(MedioPago.CREDITO);
        orden.TransicionarA(EstadoOrden.DEVOLUCION_EN_PROCESO);
        _ordenes.Precargar(orden);
        var devolucion = new Devolucion(
            DevolucionId, OrdenId, Cliente, TipoDevolucion.DEVOLUCION, esParcial, "UBI-01",
            esParcial ? 2500m : 5000m, "CRE-1", "DOC-1", "GTQ", Cliente,
            new[] { new DevolucionLinea("SKU-A", esParcial ? 1 : 2, "No le gusto", null, 2500m) });
        await _ordenes.AgregarDevolucionAsync(devolucion, CancellationToken.None);
        return (orden, devolucion);
    }

    private static EventoCreditoRecibido DevolucionAplicada(string messageId, string devolucionId = DevolucionId) =>
        new(messageId, "txn-dev", DevolucionCreditoAplicada.EventType, OrdenId, DevolucionId: devolucionId);

    private static EventoPagoRecibido ResultadoReembolso(string tipo, string messageId, string ordenId = OrdenId) =>
        new(messageId, "txn-cancel", tipo, ordenId, tipo == ReembolsoCancelacionFallido.EventType ? "Fondos insuficientes" : null);

    // ── DevolucionRegistrada lleva el medio de pago ────────────────────────────

    [Theory]
    [InlineData(MedioPago.CREDITO)]
    [InlineData(MedioPago.TARJETA)]
    public async Task DevolucionRegistrada_lleva_el_medio_de_pago_de_la_orden(MedioPago medio)
    {
        _ordenes.Precargar(OrdenEntregada(medio));

        await Devoluciones().RegistrarDevolucionAsync(
            new SolicitudDevolucion(OrdenId, TipoDevolucion.DEVOLUCION, "UBI-01", Cliente,
                new[] { new LineaDevolucionSolicitud("SKU-A", 1, "No le gusto") }),
            CancellationToken.None);

        var datos = Assert.IsType<DevolucionRegistradaData>(Assert.Single(_outbox.PorTipo(DevolucionRegistrada.EventType)).Data);
        Assert.Equal(medio, datos.MedioPago);
        Assert.True(datos.EsParcial);
    }

    // ── Cierre de la devolucion a credito ──────────────────────────────────────

    [Fact]
    public async Task DevolucionCreditoAplicada_parcial_devuelve_la_orden_a_ENTREGADA()
    {
        var (orden, devolucion) = await DevolucionEnProcesoAsync(esParcial: true);

        var resultado = await ManejadorCredito().ManejarAsync(DevolucionAplicada("EVT-DEV-1"), CancellationToken.None);

        Assert.Equal(ResultadoEventoCredito.Aplicado, resultado);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);
        Assert.Equal(EstadoDevolucion.REEMBOLSO_APLICADO, devolucion.Estado);
        Assert.Contains(_registro.Registrados, r => r.MessageId == "EVT-DEV-1");
    }

    [Fact]
    public async Task DevolucionCreditoAplicada_total_cierra_la_orden_en_DEVUELTA()
    {
        var (orden, devolucion) = await DevolucionEnProcesoAsync(esParcial: false);

        await ManejadorCredito().ManejarAsync(DevolucionAplicada("EVT-DEV-2"), CancellationToken.None);

        Assert.Equal(EstadoOrden.DEVUELTA, orden.Estado);
        Assert.Equal(EstadoDevolucion.REEMBOLSO_APLICADO, devolucion.Estado);
        Assert.Single(_outbox.PorTipo(OrdenEstadoCambiado.EventType), e => ((OrdenEstadoCambiadoData)e.Data).EstadoNuevo == EstadoOrden.DEVUELTA);
    }

    [Fact]
    public async Task DevolucionCreditoAplicada_duplicada_no_se_vuelve_a_aplicar()
    {
        var (orden, _) = await DevolucionEnProcesoAsync(esParcial: false);
        await ManejadorCredito().ManejarAsync(DevolucionAplicada("EVT-DEV-3"), CancellationToken.None);
        var eventosAntes = _outbox.Eventos.Count;

        var repetido = await ManejadorCredito().ManejarAsync(DevolucionAplicada("EVT-DEV-3"), CancellationToken.None);

        Assert.Equal(ResultadoEventoCredito.Duplicado, repetido);
        Assert.Equal(EstadoOrden.DEVUELTA, orden.Estado);
        Assert.Equal(eventosAntes, _outbox.Eventos.Count);
    }

    [Fact]
    public async Task DevolucionCreditoAplicada_de_una_devolucion_inexistente_se_confirma_sin_error()
    {
        var resultado = await ManejadorCredito().ManejarAsync(DevolucionAplicada("EVT-DEV-4", "DEV-2026-999999"), CancellationToken.None);

        Assert.Equal(ResultadoEventoCredito.Ignorado, resultado);
        Assert.Empty(_outbox.Eventos);
        Assert.Equal(1, _unidad.Confirmaciones);
    }

    // ── Estado del reembolso por cancelacion ───────────────────────────────────

    [Fact]
    public async Task Cancelar_una_orden_pagada_con_tarjeta_deja_el_reembolso_PENDIENTE()
    {
        var orden = OrdenConfirmada(MedioPago.TARJETA);
        _ordenes.Precargar(orden);

        var resultado = await Servicio().CancelarPorClienteAsync(OrdenId, new CancelarOrdenRequest(Cliente), CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Equal(EstadoReembolsoOrden.PENDIENTE, orden.EstadoReembolso);
        Assert.NotNull(orden.ReembolsoActualizadoEn);
        Assert.Equal(EstadoReembolsoOrden.PENDIENTE, resultado.Orden.EstadoReembolso);
        var cancelada = Assert.IsType<OrdenCanceladaData>(Assert.Single(_outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.True(cancelada.RequiereReembolso);
        Assert.Equal(orden.Total, cancelada.MontoReembolso);
    }

    [Fact]
    public async Task Cancelar_una_orden_a_credito_no_aplica_reembolso()
    {
        var orden = OrdenConfirmada(MedioPago.CREDITO);
        _ordenes.Precargar(orden);

        await Servicio().CancelarPorClienteAsync(OrdenId, new CancelarOrdenRequest(Cliente), CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Equal(EstadoReembolsoOrden.NO_APLICA, orden.EstadoReembolso);
        Assert.Null(orden.ReembolsoActualizadoEn);
        var cancelada = Assert.IsType<OrdenCanceladaData>(Assert.Single(_outbox.PorTipo(OrdenCancelada.EventType)).Data);
        Assert.False(cancelada.RequiereReembolso);
    }

    [Fact]
    public void Cancelar_antes_de_cobrar_no_aplica_reembolso()
    {
        var orden = NuevaOrden(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.RESERVA_SOLICITADA);
        orden.TransicionarA(EstadoOrden.CANCELADA);

        Assert.Equal(EstadoReembolsoOrden.NO_APLICA, orden.EstadoReembolso);
    }

    [Fact]
    public async Task ReembolsoCancelacionAplicado_marca_el_reembolso_APLICADO_y_se_expone_en_el_dto()
    {
        var orden = OrdenConfirmada(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        _ordenes.Precargar(orden);

        var resultado = await ManejadorPagos().ManejarAsync(
            ResultadoReembolso(ReembolsoCancelacionAplicado.EventType, "EVT-RC-1"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Aplicado, resultado);
        Assert.Equal(EstadoReembolsoOrden.APLICADO, orden.EstadoReembolso);
        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Empty(_outbox.Eventos);
        Assert.Equal(1, _unidad.Confirmaciones);
        Assert.Equal(EstadoReembolsoOrden.APLICADO, OrdenDto.Desde(orden).EstadoReembolso);
    }

    [Fact]
    public async Task ReembolsoCancelacionFallido_marca_el_reembolso_FALLIDO()
    {
        var orden = OrdenConfirmada(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        _ordenes.Precargar(orden);

        await ManejadorPagos().ManejarAsync(
            ResultadoReembolso(ReembolsoCancelacionFallido.EventType, "EVT-RC-2"), CancellationToken.None);

        Assert.Equal(EstadoReembolsoOrden.FALLIDO, orden.EstadoReembolso);
    }

    [Fact]
    public async Task Un_resultado_tardio_no_revierte_un_reembolso_aplicado()
    {
        var orden = OrdenConfirmada(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        _ordenes.Precargar(orden);

        await ManejadorPagos().ManejarAsync(ResultadoReembolso(ReembolsoCancelacionAplicado.EventType, "EVT-RC-3"), CancellationToken.None);
        var tardio = await ManejadorPagos().ManejarAsync(
            ResultadoReembolso(ReembolsoCancelacionFallido.EventType, "EVT-RC-4"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Ignorado, tardio);
        Assert.Equal(EstadoReembolsoOrden.APLICADO, orden.EstadoReembolso);
    }

    [Fact]
    public async Task Resultado_de_reembolso_duplicado_se_confirma_sin_aplicar()
    {
        var orden = OrdenConfirmada(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        _ordenes.Precargar(orden);
        await ManejadorPagos().ManejarAsync(ResultadoReembolso(ReembolsoCancelacionAplicado.EventType, "EVT-RC-5"), CancellationToken.None);

        var repetido = await ManejadorPagos().ManejarAsync(
            ResultadoReembolso(ReembolsoCancelacionAplicado.EventType, "EVT-RC-5"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Duplicado, repetido);
        Assert.Equal(1, _unidad.Confirmaciones);
    }

    [Fact]
    public async Task Resultado_de_reembolso_de_una_orden_inexistente_se_confirma_sin_error()
    {
        var resultado = await ManejadorPagos().ManejarAsync(
            ResultadoReembolso(ReembolsoCancelacionAplicado.EventType, "EVT-RC-6", "ORD-2026-999999"), CancellationToken.None);

        Assert.Equal(ResultadoEventoPago.Ignorado, resultado);
        Assert.Equal(1, _unidad.Confirmaciones);
    }

    // ── Nota de credito sobre una orden cancelada ──────────────────────────────

    [Fact]
    public async Task La_nota_de_credito_certificada_de_una_orden_cancelada_se_recibe_sin_error()
    {
        var orden = OrdenConfirmada(MedioPago.TARJETA);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        _ordenes.Precargar(orden);
        var secuenciaAntes = orden.Secuencia;
        var manejador = new ManejadorDocumentoFiscalCertificado(
            Servicio(), _ordenes, _unidad, NullLogger<ManejadorDocumentoFiscalCertificado>.Instance);

        await manejador.ManejarAsync(
            new DocumentoFiscalCertificadoData(
                Guid.NewGuid().ToString(), OrdenId, TipoDocumentoFiscal.NOTA_CREDITO, "A", "00000099", "AUT-NC", "F-1",
                Moneda.GTQ, orden.Total, DateTime.UtcNow),
            CancellationToken.None);

        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Equal(secuenciaAntes, orden.Secuencia);
        Assert.Null(orden.DocumentoFiscalId);
        Assert.Empty(_outbox.Eventos);
    }
}
