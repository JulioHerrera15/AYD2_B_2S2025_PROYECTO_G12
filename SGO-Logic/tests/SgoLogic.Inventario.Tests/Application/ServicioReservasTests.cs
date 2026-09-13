using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

public sealed class ServicioReservasTests
{
    private const string OrdenId = "ORD-2026-000777";
    private const string Sku = "SKU-LB-00871";

    private static Existencia Existencia(
        string ubicacionId,
        int cantidad,
        TipoUbicacion tipo = TipoUbicacion.CENTRO_DISTRIBUCION,
        string sku = Sku) =>
        new(sku, ubicacionId, tipo, cantidad);

    private static SolicitudReserva Solicitud(
        int cantidad = 2,
        int ttl = 900,
        string? ubicacionPreferida = null,
        MetodoEntrega metodoEntrega = MetodoEntrega.DOMICILIO) =>
        new(
            OrdenId,
            metodoEntrega,
            ubicacionPreferida,
            ttl,
            new[] { new LineaSolicitudReserva(Sku, cantidad) });

    private static (ServicioReservas Servicio,
        RepositorioExistenciasFalso Existencias,
        RepositorioReservasFalso Reservas,
        OutboxWriterFalso Outbox) Construir(
            InventarioOptions? opciones = null,
            long correlativoInicial = 777,
            params Existencia[] existencias)
    {
        var repoExistencias = new RepositorioExistenciasFalso().Precargar(existencias);
        var repoReservas = new RepositorioReservasFalso();
        var outbox = new OutboxWriterFalso();
        var servicio = new ServicioReservas(
            repoExistencias,
            repoReservas,
            outbox,
            new GeneradorCorrelativoReservaFalso(correlativoInicial),
            Options.Create(opciones ?? new InventarioOptions()));
        return (servicio, repoExistencias, repoReservas, outbox);
    }

    [Fact]
    public async Task Reservar_con_stock_suficiente_crea_la_reserva_y_encola_InventarioReservado_con_sequence_uno()
    {
        var (servicio, existencias, reservas, outbox) = Construir(
            correlativoInicial: 42,
            existencias: Existencia("CD-01", 10));

        var resultado = await servicio.ReservarAsync(Solicitud(cantidad: 3), CancellationToken.None);

        Assert.False(resultado.Rechazada);
        Assert.False(resultado.YaExistia);
        Assert.NotNull(resultado.Reserva);
        Assert.Matches(@"^RSV-\d{4}-\d{6}$", resultado.Reserva!.Id);
        Assert.Equal($"RSV-{DateTime.UtcNow:yyyy}-000042", resultado.Reserva.Id);
        Assert.Equal("CD-01", resultado.Reserva.UbicacionId);
        Assert.Equal("ACTIVA", resultado.Reserva.Estado);

        // El disponible bajo en la existencia tocada (la reserva aun no se guarda:
        // eso lo hace quien llama al servicio).
        var existenciasSku = await existencias.ListarPorSkuAsync(Sku, CancellationToken.None);
        Assert.Equal(7, existenciasSku.Single().Disponible);
        Assert.Equal(1, reservas.LlamadasAgregar);

        var evento = Assert.Single(outbox.PorTipo(InventarioReservado.EventType));
        Assert.Equal(InventarioReservado.RoutingKey, evento.RoutingKey);
        Assert.Equal("Reserva", evento.AggregateType);
        Assert.Equal(resultado.Reserva.Id, evento.AggregateId);
        Assert.Equal(1, evento.Sequence);
        var data = Assert.IsType<InventarioReservadoData>(evento.Data);
        Assert.Equal(OrdenId, data.OrdenId);
        Assert.Equal(900, data.TtlSegundosAplicado);
        Assert.Equal(data.ReservadaEn.AddSeconds(900), data.ExpiraEn);
    }

    [Fact]
    public async Task Reservar_toma_TTL_de_Inventario_recortando_el_valor_que_pide_Ordenes()
    {
        var opciones = new InventarioOptions { TtlSegundosMinimo = 300, TtlSegundosMaximo = 1800 };
        var (servicio, _, _, outbox) = Construir(opciones, existencias: Existencia("CD-01", 5));

        await servicio.ReservarAsync(Solicitud(cantidad: 1, ttl: 99999), CancellationToken.None);

        var data = Assert.IsType<InventarioReservadoData>(
            Assert.Single(outbox.PorTipo(InventarioReservado.EventType)).Data);
        Assert.Equal(1800, data.TtlSegundosAplicado);
    }

    [Fact]
    public async Task Reservar_sin_stock_suficiente_no_crea_reserva_y_encola_ReservaInventarioRechazada()
    {
        var (servicio, _, reservas, outbox) = Construir(existencias: Existencia("CD-01", 1));

        var resultado = await servicio.ReservarAsync(Solicitud(cantidad: 5), CancellationToken.None);

        Assert.True(resultado.Rechazada);
        Assert.Equal(MotivoRechazoReserva.STOCK_INSUFICIENTE, resultado.MotivoRechazo);
        Assert.Null(resultado.Reserva);
        Assert.Equal(0, reservas.LlamadasAgregar);
        Assert.Empty(outbox.PorTipo(InventarioReservado.EventType));

        var evento = Assert.Single(outbox.PorTipo(ReservaInventarioRechazada.EventType));
        Assert.Equal(ReservaInventarioRechazada.RoutingKey, evento.RoutingKey);
        Assert.Equal(OrdenId, evento.AggregateId);
        var data = Assert.IsType<ReservaInventarioRechazadaData>(evento.Data);
        Assert.Equal(MotivoRechazoReserva.STOCK_INSUFICIENTE, data.Motivo);
        var linea = Assert.Single(data.Lineas);
        Assert.Equal(Sku, linea.Sku);
        Assert.Equal(5, linea.CantidadSolicitada);
        Assert.Equal(1, linea.CantidadDisponible);
    }

    [Fact]
    public async Task Reservar_un_SKU_sin_existencia_registrada_rechaza_con_SIN_UBICACION_ELEGIBLE()
    {
        var (servicio, _, _, outbox) = Construir();

        var resultado = await servicio.ReservarAsync(Solicitud(), CancellationToken.None);

        Assert.True(resultado.Rechazada);
        Assert.Equal(MotivoRechazoReserva.SIN_UBICACION_ELEGIBLE, resultado.MotivoRechazo);
        var data = Assert.IsType<ReservaInventarioRechazadaData>(
            Assert.Single(outbox.PorTipo(ReservaInventarioRechazada.EventType)).Data);
        Assert.Equal(MotivoRechazoReserva.SIN_UBICACION_ELEGIBLE, data.Motivo);
    }

    [Fact]
    public async Task Reservar_dos_veces_la_misma_orden_devuelve_la_reserva_existente_y_no_re_emite()
    {
        var (servicio, _, reservas, outbox) = Construir(existencias: Existencia("CD-01", 10));

        var primera = await servicio.ReservarAsync(Solicitud(cantidad: 2), CancellationToken.None);
        var eventosTrasPrimera = outbox.Eventos.Count;

        var segunda = await servicio.ReservarAsync(Solicitud(cantidad: 2), CancellationToken.None);

        Assert.False(primera.YaExistia);
        Assert.True(segunda.YaExistia);
        Assert.Equal(primera.Reserva!.Id, segunda.Reserva!.Id);
        Assert.Equal(1, reservas.LlamadasAgregar);
        Assert.Equal(eventosTrasPrimera, outbox.Eventos.Count);
    }

    [Fact]
    public async Task Reservar_respeta_la_ubicacion_preferida_cuando_puede_cubrir_el_pedido()
    {
        var (servicio, _, _, outbox) = Construir(
            existencias: new[]
            {
                Existencia("CD-01", 10),
                Existencia("TDA-014", 10, TipoUbicacion.TIENDA)
            });

        var resultado = await servicio.ReservarAsync(
            Solicitud(cantidad: 2, ubicacionPreferida: "TDA-014"), CancellationToken.None);

        Assert.Equal("TDA-014", resultado.Reserva!.UbicacionId);
        var data = Assert.IsType<InventarioReservadoData>(
            Assert.Single(outbox.PorTipo(InventarioReservado.EventType)).Data);
        Assert.Equal(TipoUbicacion.TIENDA, data.TipoUbicacion);
    }

    [Fact]
    public async Task Reservar_hasta_agotar_el_disponible_y_una_reserva_mas_es_rechazada_sin_sobreventa()
    {
        var (servicio, _, _, _) = Construir(existencias: Existencia("CD-01", 2));

        var primera = await servicio.ReservarAsync(Solicitud(cantidad: 2), CancellationToken.None);
        Assert.False(primera.Rechazada);

        var segunda = await servicio.ReservarAsync(
            new SolicitudReserva(
                "ORD-2026-000778",
                MetodoEntrega.DOMICILIO,
                null,
                900,
                new[] { new LineaSolicitudReserva(Sku, 1) }),
            CancellationToken.None);

        Assert.True(segunda.Rechazada);
        Assert.Equal(MotivoRechazoReserva.STOCK_INSUFICIENTE, segunda.MotivoRechazo);
    }

    [Fact]
    public async Task Consultar_la_reserva_de_una_orden_sin_reserva_lanza_ExcepcionReservaNoEncontrada()
    {
        var (servicio, _, _, _) = Construir();

        await Assert.ThrowsAsync<ExcepcionReservaNoEncontrada>(
            () => servicio.ObtenerPorOrdenAsync("ORD-2026-000000", CancellationToken.None));
    }

    // HU-016: que tiene comprometido una ubicacion, para la vista unificada.

    [Fact]
    public async Task Listar_las_reservas_de_una_ubicacion_puede_filtrar_por_estado()
    {
        var (servicio, _, reservas, _) = Construir();

        var activa = ReservaEn("RSV-2026-000001", "ORD-2026-000001", "TDA-07");
        var expirada = ReservaEn("RSV-2026-000002", "ORD-2026-000002", "TDA-07");
        expirada.MarcarExpirada();

        reservas.Precargar(activa);
        reservas.Precargar(expirada);
        reservas.Precargar(ReservaEn("RSV-2026-000003", "ORD-2026-000003", "CD-01"));

        var todas = await servicio.ListarPorUbicacionAsync("TDA-07", null, CancellationToken.None);
        Assert.Equal(2, todas.Count);

        var activas = await servicio.ListarPorUbicacionAsync(
            "TDA-07",
            EstadoReserva.ACTIVA,
            CancellationToken.None);

        Assert.Equal("RSV-2026-000001", Assert.Single(activas).Id);
    }

    private static Reserva ReservaEn(string id, string ordenId, string ubicacionId) =>
        Reserva.Crear(
            id,
            ordenId,
            ubicacionId,
            TipoUbicacion.TIENDA,
            900,
            new[] { (Sku, 1) },
            DateTime.UtcNow);
}
