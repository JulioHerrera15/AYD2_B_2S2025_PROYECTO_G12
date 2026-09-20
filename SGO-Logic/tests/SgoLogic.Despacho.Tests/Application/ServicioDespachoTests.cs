using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Despacho.Api.Application.Despacho;
using SgoLogic.Despacho.Api.Domain;
using SgoLogic.Despacho.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Despacho.Tests.Application;

/// <summary>
/// HU-011, HU-012, HU-019 y HU-020 desde el servicio: a quien le pregunta el origen,
/// cuando pide guia, que pasa si LogiExpress no responde y que eventos salen.
/// </summary>
public sealed class ServicioDespachoTests
{
    private const string OrdenId = "ORD-2026-000001";
    private const string ReservaId = "RSV-2026-000001";

    private static (ServicioDespacho Servicio,
        RepositorioDespachosFalso Despachos,
        ClienteInventarioFalso Inventario,
        ClienteLogiExpressFalso LogiExpress,
        OutboxWriterFalso Outbox) Construir(ClienteInventarioFalso? inventario = null)
    {
        var despachos = new RepositorioDespachosFalso();
        var inv = inventario ?? new ClienteInventarioFalso();
        var logi = new ClienteLogiExpressFalso();
        var outbox = new OutboxWriterFalso();

        var servicio = new ServicioDespacho(
            despachos, inv, logi,
            new GeneradorCorrelativoDespachoFalso(),
            outbox,
            NullLogger<ServicioDespacho>.Instance);

        return (servicio, despachos, inv, logi, outbox);
    }

    private static SolicitudDespacho Solicitud(
        MetodoEntrega metodo = MetodoEntrega.DOMICILIO,
        string? ubicacionSugerida = null,
        string? ubicacionRecogida = null) =>
        new(
            OrdenId,
            ReservaId,
            metodo,
            ubicacionSugerida,
            ubicacionSugerida is null ? null : TipoUbicacion.CENTRO_DISTRIBUCION,
            ubicacionRecogida,
            metodo == MetodoEntrega.DOMICILIO ? "zona 10" : null,
            new[] { new LineaDespachoDto("SKU-A", "lampara", 2) });

    [Fact]
    public async Task El_origen_sale_de_donde_Inventario_reservo_de_verdad()
    {
        // Lo importante: aunque Ordenes sugiera otra cosa, manda Inventario, porque
        // ahi es donde se descontaron las unidades.
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, inv, _, _) = Construir(inventario);

        var resultado = await servicio.SolicitarAsync(
            Solicitud(ubicacionSugerida: "CD-99"), CancellationToken.None);

        Assert.Equal("CD-07", resultado.UbicacionOrigenId);
        Assert.Equal(1, inv.Llamadas);
    }

    [Fact]
    public async Task Si_Inventario_no_sabe_de_la_reserva_se_usa_la_sugerencia()
    {
        var (servicio, _, _, _, _) = Construir();

        var resultado = await servicio.SolicitarAsync(
            Solicitud(ubicacionSugerida: "CD-99"), CancellationToken.None);

        Assert.Equal("CD-99", resultado.UbicacionOrigenId);
    }

    [Fact]
    public async Task Sin_reserva_ni_sugerencia_no_se_inventa_un_origen()
    {
        // Se deja subir para que la cola reintente: puede que la reserva aun no sea
        // visible. Inventar una ubicacion mandaria a preparar el pedido donde no esta.
        var (servicio, _, _, _, _) = Construir();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.SolicitarAsync(Solicitud(), CancellationToken.None));
    }

    [Fact]
    public async Task Domicilio_con_LogiExpress_bien_deja_la_guia_asignada()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, logi, outbox) = Construir(inventario);

        var resultado = await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        Assert.Equal(nameof(EstadoDespacho.GUIA_ASIGNADA), resultado.Estado);
        Assert.NotNull(resultado.GuiaId);
        Assert.Equal(1, logi.Solicitudes);
        Assert.Single(outbox.PorTipo(GuiaAsignada.EventType));
    }

    [Fact]
    public async Task Un_timeout_de_LogiExpress_no_aborta_nada_y_deja_la_guia_pendiente()
    {
        // Criterio de HU-011: el despacho fisico puede iniciar igual.
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, logi, outbox) = Construir(inventario);
        logi.Modo = ClienteLogiExpressFalso.Comportamiento.AgotaTiempo;

        var resultado = await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        Assert.Equal(nameof(EstadoDespacho.GUIA_PENDIENTE), resultado.Estado);
        Assert.Null(resultado.GuiaId);
        Assert.Single(outbox.PorTipo(GuiaPendiente.EventType));
        Assert.Empty(outbox.PorTipo(GuiaAsignada.EventType));

        // Y el origen quedo determinado igual: se sabe que preparar y donde.
        Assert.Equal("CD-07", resultado.UbicacionOrigenId);
    }

    [Fact]
    public async Task Recogida_en_tienda_no_llama_a_LogiExpress()
    {
        // HU-020, criterio literal: si el cliente eligio recogida, no se solicita guia.
        var (servicio, _, _, logi, outbox) = Construir();

        var resultado = await servicio.SolicitarAsync(
            Solicitud(MetodoEntrega.RECOGIDA_TIENDA, ubicacionRecogida: "TDA-07"), CancellationToken.None);

        Assert.Equal(nameof(EstadoDespacho.RECOGIDA_TIENDA), resultado.Estado);
        Assert.Equal(0, logi.Solicitudes);
        Assert.Empty(outbox.PorTipo(GuiaAsignada.EventType));
        Assert.Empty(outbox.PorTipo(GuiaPendiente.EventType));
    }

    [Fact]
    public async Task Siempre_se_pide_preparacion_al_operador()
    {
        // HU-019: haya guia o no, alguien tiene que alistar el pedido.
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, logi, outbox) = Construir(inventario);
        logi.Modo = ClienteLogiExpressFalso.Comportamiento.AgotaTiempo;

        await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        var preparacion = Assert.Single(outbox.PorTipo(PreparacionSolicitada.EventType));
        var datos = Assert.IsType<PreparacionSolicitadaData>(preparacion.Data);
        Assert.Equal("CD-07", datos.UbicacionOrigenId);
        Assert.Single(datos.Lineas);
    }

    [Fact]
    public async Task Pedir_dos_veces_el_mismo_despacho_no_lo_duplica()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, despachos, _, logi, outbox) = Construir(inventario);

        var primero = await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);
        var segundo = await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        Assert.Equal(primero.Id, segundo.Id);
        Assert.Single(despachos.Todos);

        // Y no se pide una segunda guia por el reintento.
        Assert.Equal(1, logi.Solicitudes);
        Assert.Single(outbox.PorTipo(OrigenDespachoDeterminado.EventType));
    }

    [Fact]
    public async Task Cambiar_a_recogida_anula_la_guia_emitida()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, logi, _) = Construir(inventario);

        var original = await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        var resultado = await servicio.CambiarMetodoEntregaAsync(
            new SolicitudCambioMetodo(OrdenId, MetodoEntrega.RECOGIDA_TIENDA, "TDA-07", null),
            CancellationToken.None);

        Assert.Equal(nameof(EstadoDespacho.RECOGIDA_TIENDA), resultado.Estado);
        Assert.Null(resultado.GuiaId);
        Assert.Contains(original.GuiaId!, logi.Anuladas);
    }

    [Fact]
    public async Task Cambiar_a_domicilio_pide_una_guia_nueva()
    {
        var (servicio, _, _, logi, _) = Construir();

        await servicio.SolicitarAsync(
            Solicitud(MetodoEntrega.RECOGIDA_TIENDA, ubicacionRecogida: "TDA-07"), CancellationToken.None);

        Assert.Equal(0, logi.Solicitudes);

        var resultado = await servicio.CambiarMetodoEntregaAsync(
            new SolicitudCambioMetodo(OrdenId, MetodoEntrega.DOMICILIO, null, "zona 9"),
            CancellationToken.None);

        Assert.Equal(nameof(EstadoDespacho.GUIA_ASIGNADA), resultado.Estado);
        Assert.Equal(1, logi.Solicitudes);
    }

    [Fact]
    public async Task Cambiar_al_mismo_metodo_no_hace_nada()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, logi, _) = Construir(inventario);

        await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        await servicio.CambiarMetodoEntregaAsync(
            new SolicitudCambioMetodo(OrdenId, MetodoEntrega.DOMICILIO, null, "zona 10"),
            CancellationToken.None);

        Assert.Equal(1, logi.Solicitudes);
        Assert.Empty(logi.Anuladas);
    }

    [Fact]
    public async Task Un_estado_de_entrega_nuevo_emite_evento()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, _, outbox) = Construir(inventario);
        await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        await servicio.ActualizarEstadoAsync(
            OrdenId, EstadoDespacho.EN_TRANSITO, "salio del CD", DateTime.UtcNow, CancellationToken.None);

        var evento = Assert.Single(outbox.PorTipo(DespachoEstadoActualizado.EventType));
        var datos = Assert.IsType<DespachoEstadoActualizadoData>(evento.Data);
        Assert.Equal(nameof(EstadoDespacho.EN_TRANSITO), datos.EstadoNuevo);
    }

    [Fact]
    public async Task Repetir_el_webhook_con_el_mismo_estado_no_emite_de_nuevo()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, _, outbox) = Construir(inventario);
        await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        await servicio.ActualizarEstadoAsync(OrdenId, EstadoDespacho.EN_TRANSITO, null, DateTime.UtcNow, CancellationToken.None);
        await servicio.ActualizarEstadoAsync(OrdenId, EstadoDespacho.EN_TRANSITO, null, DateTime.UtcNow, CancellationToken.None);

        Assert.Single(outbox.PorTipo(DespachoEstadoActualizado.EventType));
    }

    [Fact]
    public async Task Listo_para_recoger_avisa_al_cliente()
    {
        // HU-020: la tienda ya lo tiene listo y el cliente debe enterarse.
        var (servicio, _, _, _, outbox) = Construir();
        await servicio.SolicitarAsync(
            Solicitud(MetodoEntrega.RECOGIDA_TIENDA, ubicacionRecogida: "TDA-07"), CancellationToken.None);

        await servicio.ActualizarEstadoAsync(
            OrdenId, EstadoDespacho.LISTA_PARA_RECOGER, "mostrador 3", DateTime.UtcNow, CancellationToken.None);

        var aviso = Assert.Single(outbox.PorTipo(PedidoListoParaRecoger.EventType));
        var datos = Assert.IsType<PedidoListoParaRecogerData>(aviso.Data);
        Assert.Equal("TDA-07", datos.UbicacionRecogidaId);
    }

    [Fact]
    public async Task Cancelar_la_orden_anula_el_despacho_y_su_guia()
    {
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, despachos, _, logi, _) = Construir(inventario);
        var original = await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        await servicio.AnularPorOrdenCanceladaAsync(OrdenId, CancellationToken.None);

        Assert.Equal(EstadoDespacho.ANULADA, despachos.Todos.Single().Estado);
        Assert.Contains(original.GuiaId!, logi.Anuladas);
    }

    [Fact]
    public async Task Consultar_un_despacho_que_no_existe_lo_dice()
    {
        var (servicio, _, _, _, _) = Construir();

        await Assert.ThrowsAsync<ExcepcionDespachoNoEncontrado>(
            () => servicio.ObtenerPorOrdenAsync("ORD-INEXISTENTE", CancellationToken.None));
    }

    [Fact]
    public async Task La_cola_de_preparacion_filtra_por_ubicacion()
    {
        // HU-019: el operador de una tienda solo ve lo suyo.
        var inventario = new ClienteInventarioFalso().Con(OrdenId, "CD-07", TipoUbicacion.CENTRO_DISTRIBUCION);
        var (servicio, _, _, _, _) = Construir(inventario);
        await servicio.SolicitarAsync(Solicitud(), CancellationToken.None);

        var enCd07 = await servicio.ListarPorUbicacionOrigenAsync("CD-07", null, CancellationToken.None);
        var enOtra = await servicio.ListarPorUbicacionOrigenAsync("TDA-99", null, CancellationToken.None);

        Assert.Single(enCd07);
        Assert.Empty(enOtra);
    }
}
