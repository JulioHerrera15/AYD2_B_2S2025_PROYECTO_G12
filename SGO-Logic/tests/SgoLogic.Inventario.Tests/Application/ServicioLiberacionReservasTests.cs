using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// HU-003: un cobro rechazado devuelve el stock de inmediato, sin esperar los 15
/// minutos del TTL.
/// </summary>
public sealed class ServicioLiberacionReservasTests
{
    private const string Sku = "SKU-LB-00871";
    private const string Ubicacion = "CD-01";
    private const string OrdenId = "ORD-2026-000001";

    private static (ServicioLiberacionReservas Servicio,
        RepositorioReservasFalso Reservas,
        RepositorioExistenciasFalso Existencias,
        OutboxWriterFalso Outbox) Construir(Existencia? existencia = null, Reserva? reserva = null)
    {
        var existencias = new RepositorioExistenciasFalso();
        if (existencia is not null)
        {
            existencias.Precargar(existencia);
        }

        var reservas = new RepositorioReservasFalso();
        if (reserva is not null)
        {
            reservas.Precargar(reserva);
        }

        var outbox = new OutboxWriterFalso();

        return (new ServicioLiberacionReservas(reservas, existencias, outbox, NullLogger<ServicioLiberacionReservas>.Instance),
            reservas, existencias, outbox);
    }

    private static Existencia ExistenciaConReserva(int fisica, int reservada)
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, fisica);
        existencia.Reservar(reservada);
        return existencia;
    }

    private static Reserva ReservaActiva(int cantidad = 4) =>
        Reserva.Crear(
            "RSV-2026-000001",
            OrdenId,
            Ubicacion,
            TipoUbicacion.CENTRO_DISTRIBUCION,
            900,
            new[] { (Sku, cantidad) },
            DateTime.UtcNow);

    [Fact]
    public async Task Liberar_devuelve_las_unidades_al_disponible()
    {
        var existencia = ExistenciaConReserva(10, 4);
        var (servicio, _, _, _) = Construir(existencia, ReservaActiva());

        var libero = await servicio.LiberarAsync(
            OrdenId, MotivoLiberacionReserva.COBRO_RECHAZADO, CancellationToken.None);

        Assert.True(libero);
        Assert.Equal(0, existencia.CantidadReservada);

        // La cantidad fisica no se toca: la reserva era un compromiso, no una salida.
        Assert.Equal(10, existencia.CantidadFisica);
        Assert.Equal(10, existencia.Disponible);
    }

    [Fact]
    public async Task Liberar_marca_la_reserva_y_encola_el_evento()
    {
        var reserva = ReservaActiva();
        var (servicio, _, _, outbox) = Construir(ExistenciaConReserva(10, 4), reserva);

        await servicio.LiberarAsync(OrdenId, MotivoLiberacionReserva.COBRO_RECHAZADO, CancellationToken.None);

        Assert.Equal(EstadoReserva.LIBERADA, reserva.Estado);

        var evento = Assert.Single(outbox.PorTipo(ReservaInventarioLiberada.EventType));
        var datos = Assert.IsType<ReservaInventarioLiberadaData>(evento.Data);
        Assert.Equal(MotivoLiberacionReserva.COBRO_RECHAZADO, datos.Motivo);
        Assert.Equal(Ubicacion, datos.UbicacionId);
        Assert.Single(datos.Lineas);
    }

    [Fact]
    public async Task Una_orden_sin_reserva_no_es_un_error()
    {
        // Pasa de verdad: la orden se cancelo antes de que Inventario reservara, o la
        // reserva se rechazo por falta de stock. Reintentar no arreglaria nada.
        var (servicio, _, _, outbox) = Construir();

        var libero = await servicio.LiberarAsync(
            OrdenId, MotivoLiberacionReserva.CANCELACION_CLIENTE, CancellationToken.None);

        Assert.False(libero);
        Assert.Empty(outbox.Eventos);
    }

    [Fact]
    public async Task Una_reserva_ya_expirada_no_se_libera_dos_veces()
    {
        // El barrido de TTL (HU-002) pudo ganarle al mensaje. Si se liberara otra vez,
        // se devolverian unidades que ya se habian devuelto.
        var existencia = ExistenciaConReserva(10, 4);
        var reserva = ReservaActiva();
        reserva.MarcarExpirada();

        var (servicio, _, _, outbox) = Construir(existencia, reserva);

        var libero = await servicio.LiberarAsync(
            OrdenId, MotivoLiberacionReserva.COBRO_RECHAZADO, CancellationToken.None);

        Assert.False(libero);
        Assert.Equal(4, existencia.CantidadReservada);
        Assert.Empty(outbox.Eventos);
    }

    [Fact]
    public async Task Liberar_una_reserva_confirmada_devuelve_las_unidades()
    {
        // Cancelacion del cliente despues de pagar: la reserva ya estaba CONFIRMADA.
        var existencia = ExistenciaConReserva(10, 4);
        var reserva = ReservaActiva();
        reserva.Confirmar();
        var (servicio, _, _, outbox) = Construir(existencia, reserva);

        var libero = await servicio.LiberarAsync(
            OrdenId, MotivoLiberacionReserva.CANCELACION_CLIENTE, CancellationToken.None);

        Assert.True(libero);
        Assert.Equal(EstadoReserva.LIBERADA, reserva.Estado);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Equal(10, existencia.CantidadFisica);
        Assert.Single(outbox.PorTipo(ReservaInventarioLiberada.EventType));
    }

    [Fact]
    public async Task Una_reserva_ya_entregada_no_se_libera()
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, 6);
        var reserva = ReservaActiva();
        reserva.MarcarEntregada();
        var (servicio, _, _, outbox) = Construir(existencia, reserva);

        var libero = await servicio.LiberarAsync(
            OrdenId, MotivoLiberacionReserva.CANCELACION_CLIENTE, CancellationToken.None);

        Assert.False(libero);
        Assert.Equal(EstadoReserva.ENTREGADA, reserva.Estado);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Empty(outbox.Eventos);
    }

    [Fact]
    public async Task El_mensaje_repetido_no_duplica_la_liberacion()
    {
        var existencia = ExistenciaConReserva(10, 4);
        var (servicio, _, _, outbox) = Construir(existencia, ReservaActiva());

        await servicio.LiberarAsync(OrdenId, MotivoLiberacionReserva.COBRO_RECHAZADO, CancellationToken.None);
        await servicio.LiberarAsync(OrdenId, MotivoLiberacionReserva.COBRO_RECHAZADO, CancellationToken.None);

        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Single(outbox.PorTipo(ReservaInventarioLiberada.EventType));
    }

    [Fact]
    public async Task Si_la_existencia_desaparecio_la_reserva_se_libera_igual()
    {
        // No puede quedar una reserva reteniendo stock para siempre por un dato roto.
        var (servicio, _, _, outbox) = Construir(existencia: null, reserva: ReservaActiva());

        var libero = await servicio.LiberarAsync(
            OrdenId, MotivoLiberacionReserva.COBRO_RECHAZADO, CancellationToken.None);

        Assert.True(libero);
        Assert.Single(outbox.PorTipo(ReservaInventarioLiberada.EventType));
    }
}
