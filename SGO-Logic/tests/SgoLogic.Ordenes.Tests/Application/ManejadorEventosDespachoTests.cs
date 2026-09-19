using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Pruebas del avance de la orden por el tramo de entrega al consumir los eventos
/// de Despacho: mapeo de cada estado, deduplicacion, eventos fuera de orden,
/// saltos de estado y recogida en tienda. El manejador se arma con los mismos
/// dobles en memoria que el resto del contexto.
/// </summary>
public sealed class ManejadorEventosDespachoTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string TransactionId = "a1c4e8b2-77d3-4f19-b5aa-0e93c62d1845";

    private static Orden CrearOrden(MetodoEntrega metodo = MetodoEntrega.DOMICILIO) => new(
        OrdenId,
        "000777",
        "CLI-000045",
        Canal.WEB,
        metodo,
        Moneda.GTQ,
        MedioPago.TARJETA,
        new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        150.00m,
        ubicacionSolicitadaId: metodo == MetodoEntrega.RECOGIDA_TIENDA ? "TDA-014" : null);

    private static Orden OrdenEn(EstadoOrden estado, MetodoEntrega metodo = MetodoEntrega.DOMICILIO)
    {
        var orden = CrearOrden(metodo);
        foreach (var paso in CaminoHasta(estado, metodo))
        {
            orden.TransicionarA(paso);
        }

        return orden;
    }

    private static IEnumerable<EstadoOrden> CaminoHasta(EstadoOrden estado, MetodoEntrega metodo)
    {
        // EN_TRANSITO y LISTA_PARA_RECOGER son alternativas: cual sigue a
        // EN_PREPARACION depende del metodo de entrega.
        var etapaSalida = metodo == MetodoEntrega.DOMICILIO
            ? EstadoOrden.EN_TRANSITO
            : EstadoOrden.LISTA_PARA_RECOGER;

        var camino = new[]
        {
            EstadoOrden.RESERVA_SOLICITADA,
            EstadoOrden.INVENTARIO_RESERVADO,
            EstadoOrden.COBRO_SOLICITADO,
            EstadoOrden.PAGO_APLICADO,
            EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
            EstadoOrden.CONFIRMADA,
            EstadoOrden.DESPACHO_SOLICITADO,
            EstadoOrden.EN_PREPARACION,
            etapaSalida,
            EstadoOrden.ENTREGADA
        };

        return camino.Take(Array.IndexOf(camino, estado) + 1);
    }

    private static EventoDespachoRecibido Actualizado(
        string estadoDespacho,
        string messageId = "EVT-1",
        string? detalle = null) =>
        new(messageId, TransactionId, DespachoEstadoActualizado.EventType, OrdenId, estadoDespacho, detalle);

    private static EventoDespachoRecibido ListoParaRecoger(string messageId = "EVT-LISTO") =>
        new(messageId, TransactionId, PedidoListoParaRecoger.EventType, OrdenId, "LISTA_PARA_RECOGER", null);

    private static (ManejadorEventosDespacho Manejador,
        RepositorioOrdenesFalso Ordenes,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa Unidad,
        RegistroMensajesProcesadosFalso Registro) Construir()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var outbox = new OutboxWriterFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var registro = new RegistroMensajesProcesadosFalso();
        var manejador = new ManejadorEventosDespacho(
            registro,
            ordenes,
            outbox,
            unidad,
            NullLogger<ManejadorEventosDespacho>.Instance);
        return (manejador, ordenes, outbox, unidad, registro);
    }

    [Theory]
    [InlineData("EN_PREPARACION", EstadoOrden.DESPACHO_SOLICITADO, EstadoOrden.EN_PREPARACION)]
    [InlineData("EN_TRANSITO", EstadoOrden.EN_PREPARACION, EstadoOrden.EN_TRANSITO)]
    [InlineData("ENTREGADA", EstadoOrden.EN_TRANSITO, EstadoOrden.ENTREGADA)]
    public async Task Cada_estado_de_entrega_a_domicilio_lleva_la_orden_a_su_equivalente(
        string estadoDespacho,
        EstadoOrden estadoInicial,
        EstadoOrden estadoEsperado)
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEn(estadoInicial);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Actualizado(estadoDespacho), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, resultado);
        Assert.Equal(estadoEsperado, orden.Estado);

        var estadoCambiado = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(OrdenEstadoCambiado.RoutingKey, estadoCambiado.RoutingKey);
        Assert.Equal(orden.Id, estadoCambiado.AggregateId);
        Assert.Equal(orden.Secuencia, estadoCambiado.Sequence);
        var data = Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data);
        Assert.Equal(estadoInicial, data.EstadoAnterior);
        Assert.Equal(estadoEsperado, data.EstadoNuevo);
        Assert.Equal(PasoSaga.DESPACHO, data.PasoSaga);
        Assert.False(data.EsCompensacion);

        Assert.Equal(1, unidad.Confirmaciones);
        var marca = Assert.Single(registro.Registrados);
        Assert.Equal("EVT-1", marca.MessageId);
    }

    [Fact]
    public async Task LISTA_PARA_RECOGER_lleva_la_orden_de_recogida_a_LISTA_PARA_RECOGER()
    {
        var (manejador, ordenes, outbox, _, _) = Construir();
        var orden = OrdenEn(EstadoOrden.EN_PREPARACION, MetodoEntrega.RECOGIDA_TIENDA);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Actualizado("LISTA_PARA_RECOGER"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, resultado);
        Assert.Equal(EstadoOrden.LISTA_PARA_RECOGER, orden.Estado);
        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
    }

    [Fact]
    public async Task PedidoListoParaRecoger_tambien_lleva_la_orden_a_LISTA_PARA_RECOGER()
    {
        var (manejador, ordenes, outbox, _, registro) = Construir();
        var orden = OrdenEn(EstadoOrden.EN_PREPARACION, MetodoEntrega.RECOGIDA_TIENDA);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(ListoParaRecoger(), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, resultado);
        Assert.Equal(EstadoOrden.LISTA_PARA_RECOGER, orden.Estado);
        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(PedidoListoParaRecoger.EventType, Assert.Single(registro.Registrados).TipoEvento);
    }

    [Fact]
    public async Task Despacho_emite_dos_eventos_por_lista_para_recoger_y_el_segundo_no_repite_la_transicion()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEn(EstadoOrden.EN_PREPARACION, MetodoEntrega.RECOGIDA_TIENDA);
        ordenes.Precargar(orden);

        var primero = await manejador.ManejarAsync(Actualizado("LISTA_PARA_RECOGER", "EVT-A"), CancellationToken.None);
        var segundo = await manejador.ManejarAsync(ListoParaRecoger("EVT-B"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, primero);
        Assert.Equal(ResultadoEventoDespacho.Ignorado, segundo);
        Assert.Equal(EstadoOrden.LISTA_PARA_RECOGER, orden.Estado);
        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Theory]
    [InlineData("ORIGEN_PENDIENTE")]
    [InlineData("RECOGIDA_TIENDA")]
    [InlineData("GUIA_SOLICITADA")]
    [InlineData("GUIA_ASIGNADA")]
    [InlineData("GUIA_PENDIENTE")]
    [InlineData("INCIDENCIA")]
    [InlineData("ANULADA")]
    public async Task Un_estado_de_despacho_sin_equivalente_no_mueve_la_orden_ni_escribe(string estadoDespacho)
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        ordenes.Precargar(orden);
        var transicionesAntes = orden.Transiciones.Count;

        var resultado = await manejador.ManejarAsync(Actualizado(estadoDespacho), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Ignorado, resultado);
        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Empty(outbox.Eventos);
        Assert.Empty(registro.Registrados);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Un_estado_de_despacho_desconocido_se_ignora_sin_excepcion()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Actualizado("EXTRAVIADA"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Ignorado, resultado);
        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task El_mismo_messageId_dos_veces_aplica_la_transicion_una_sola_vez()
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        ordenes.Precargar(orden);

        var primera = await manejador.ManejarAsync(Actualizado("EN_PREPARACION", "EVT-1"), CancellationToken.None);
        var segunda = await manejador.ManejarAsync(Actualizado("EN_PREPARACION", "EVT-1"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, primera);
        Assert.Equal(ResultadoEventoDespacho.Duplicado, segunda);
        Assert.Equal(EstadoOrden.EN_PREPARACION, orden.Estado);
        Assert.Single(orden.Transiciones.Where(t => t.EstadoNuevo == EstadoOrden.EN_PREPARACION));
        Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(1, unidad.Confirmaciones);
        Assert.Single(registro.Registrados);
    }

    [Fact]
    public async Task Un_messageId_ya_registrado_se_descarta_sin_tocar_la_orden()
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        ordenes.Precargar(orden);
        registro.Precargar("EVT-VIEJO");

        var resultado = await manejador.ManejarAsync(Actualizado("EN_PREPARACION", "EVT-VIEJO"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Duplicado, resultado);
        Assert.Equal(EstadoOrden.DESPACHO_SOLICITADO, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Theory]
    [InlineData("EN_PREPARACION", EstadoOrden.EN_TRANSITO)]
    [InlineData("EN_PREPARACION", EstadoOrden.ENTREGADA)]
    [InlineData("EN_TRANSITO", EstadoOrden.ENTREGADA)]
    [InlineData("EN_TRANSITO", EstadoOrden.EN_TRANSITO)]
    [InlineData("ENTREGADA", EstadoOrden.ENTREGADA)]
    public async Task Un_evento_fuera_de_orden_sobre_un_estado_ya_superado_se_ignora_sin_excepcion(
        string estadoDespacho,
        EstadoOrden estadoActual)
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEn(estadoActual);
        ordenes.Precargar(orden);
        var transicionesAntes = orden.Transiciones.Count;

        var resultado = await manejador.ManejarAsync(Actualizado(estadoDespacho), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Ignorado, resultado);
        Assert.Equal(estadoActual, orden.Estado);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Empty(outbox.Eventos);
        Assert.Empty(registro.Registrados);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Un_estado_de_entrega_tardio_no_revive_una_orden_devuelta()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEn(EstadoOrden.ENTREGADA);
        orden.TransicionarA(EstadoOrden.DEVOLUCION_EN_PROCESO);
        orden.TransicionarA(EstadoOrden.DEVUELTA);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Actualizado("EN_TRANSITO"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Ignorado, resultado);
        Assert.Equal(EstadoOrden.DEVUELTA, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Un_estado_de_entrega_sobre_una_orden_cancelada_se_ignora_sin_excepcion()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        orden.TransicionarA(EstadoOrden.CANCELADA);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Actualizado("ENTREGADA"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Ignorado, resultado);
        Assert.Equal(EstadoOrden.CANCELADA, orden.Estado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Un_evento_para_una_orden_inexistente_se_ignora_sin_excepcion()
    {
        var (manejador, _, outbox, unidad, _) = Construir();

        var resultado = await manejador.ManejarAsync(Actualizado("EN_PREPARACION"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Ignorado, resultado);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task ENTREGADA_saltandose_los_intermedios_a_domicilio_aplica_las_tres_transiciones_en_una_transaccion()
    {
        var (manejador, ordenes, outbox, unidad, registro) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        ordenes.Precargar(orden);
        var secuenciaInicial = orden.Secuencia;

        var resultado = await manejador.ManejarAsync(Actualizado("ENTREGADA"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, resultado);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);

        var recorrido = orden.Transiciones.TakeLast(3).Select(t => t.EstadoNuevo).ToArray();
        Assert.Equal(
            new[] { EstadoOrden.EN_PREPARACION, EstadoOrden.EN_TRANSITO, EstadoOrden.ENTREGADA },
            recorrido);

        var eventos = outbox.PorTipo(OrdenEstadoCambiado.EventType).ToList();
        Assert.Equal(3, eventos.Count);
        Assert.Equal(
            new[] { secuenciaInicial + 1, secuenciaInicial + 2, secuenciaInicial + 3 },
            eventos.Select(e => e.Sequence).ToArray());
        Assert.Equal(
            new[] { EstadoOrden.EN_PREPARACION, EstadoOrden.EN_TRANSITO, EstadoOrden.ENTREGADA },
            eventos.Select(e => Assert.IsType<OrdenEstadoCambiadoData>(e.Data).EstadoNuevo).ToArray());

        // Una sola transaccion y una sola marca de idempotencia para las tres.
        Assert.Equal(1, unidad.Confirmaciones);
        Assert.Single(registro.Registrados);
    }

    [Fact]
    public async Task Recogida_en_tienda_confirmada_por_Despacho_lleva_la_orden_a_ENTREGADA_pasando_por_LISTA_PARA_RECOGER()
    {
        var (manejador, ordenes, outbox, unidad, _) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO, MetodoEntrega.RECOGIDA_TIENDA);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(
            Actualizado("ENTREGADA", detalle: "Cliente recogio en tienda"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, resultado);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);

        // Recogida en tienda no pasa por EN_TRANSITO.
        var recorrido = orden.Transiciones.TakeLast(3).Select(t => t.EstadoNuevo).ToArray();
        Assert.Equal(
            new[] { EstadoOrden.EN_PREPARACION, EstadoOrden.LISTA_PARA_RECOGER, EstadoOrden.ENTREGADA },
            recorrido);
        Assert.DoesNotContain(orden.Transiciones, t => t.EstadoNuevo == EstadoOrden.EN_TRANSITO);

        var ultimo = outbox.PorTipo(OrdenEstadoCambiado.EventType).Last();
        var data = Assert.IsType<OrdenEstadoCambiadoData>(ultimo.Data);
        Assert.Equal(EstadoOrden.LISTA_PARA_RECOGER, data.EstadoAnterior);
        Assert.Equal(EstadoOrden.ENTREGADA, data.EstadoNuevo);
        Assert.Equal("Cliente recogio en tienda", data.Motivo);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Recogida_en_tienda_desde_LISTA_PARA_RECOGER_aplica_solo_ENTREGADA()
    {
        var (manejador, ordenes, outbox, _, _) = Construir();
        var orden = OrdenEn(EstadoOrden.LISTA_PARA_RECOGER, MetodoEntrega.RECOGIDA_TIENDA);
        ordenes.Precargar(orden);

        var resultado = await manejador.ManejarAsync(Actualizado("ENTREGADA"), CancellationToken.None);

        Assert.Equal(ResultadoEventoDespacho.Aplicado, resultado);
        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);
        var estadoCambiado = Assert.Single(outbox.PorTipo(OrdenEstadoCambiado.EventType));
        Assert.Equal(
            EstadoOrden.LISTA_PARA_RECOGER,
            Assert.IsType<OrdenEstadoCambiadoData>(estadoCambiado.Data).EstadoAnterior);
    }

    [Fact]
    public async Task Cada_transicion_aplicada_pasa_por_el_mapa_de_eventos_de_saga_sin_eventos_extra()
    {
        var (manejador, ordenes, outbox, _, _) = Construir();
        var orden = OrdenEn(EstadoOrden.DESPACHO_SOLICITADO);
        ordenes.Precargar(orden);

        await manejador.ManejarAsync(Actualizado("ENTREGADA"), CancellationToken.None);

        // Ningun estado del tramo de entrega tiene evento de saga propio: solo
        // OrdenEstadoCambiado, que es lo que consumen Consulta de Estado,
        // Notificaciones y Auditoria.
        Assert.All(outbox.Eventos, e => Assert.Equal(OrdenEstadoCambiado.EventType, e.EventType));
        Assert.Empty(outbox.PorTipo(LiberacionReservaSolicitada.EventType));
    }
}
