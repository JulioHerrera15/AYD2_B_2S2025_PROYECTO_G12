using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// HU-002: las reservas con TTL vencido se liberan solas y, si una falla, se
/// reintenta sin liberar prematuramente.
/// </summary>
public sealed class ServicioExpiracionReservasTests
{
    private const string Sku = "SKU-LB-00871";
    private const string Ubicacion = "CD-01";

    private static Existencia ExistenciaReservada(int fisica, int aReservar, string sku = Sku, string ubicacion = Ubicacion)
    {
        var existencia = new Existencia(sku, ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, fisica);
        existencia.Reservar(aReservar);
        return existencia;
    }

    private static Reserva ReservaVencida(
        string id,
        string ordenId,
        int cantidad = 3,
        string sku = Sku,
        string ubicacion = Ubicacion) =>
        Reserva.Crear(
            id,
            ordenId,
            ubicacion,
            TipoUbicacion.CENTRO_DISTRIBUCION,
            60,
            new[] { (sku, cantidad) },
            DateTime.UtcNow.AddMinutes(-10));

    private static (ServicioExpiracionReservas Servicio,
        RepositorioReservasFalso Reservas,
        OutboxWriterFalso Outbox,
        UnidadDeTrabajoFalsa UnidadDeTrabajo) Construir(
            RepositorioExistenciasFalso existencias,
            params Reserva[] reservas)
    {
        var repoReservas = new RepositorioReservasFalso();
        foreach (var reserva in reservas)
        {
            repoReservas.Precargar(reserva);
        }

        var outbox = new OutboxWriterFalso();
        var unidadDeTrabajo = new UnidadDeTrabajoFalsa();

        var servicio = new ServicioExpiracionReservas(
            repoReservas,
            existencias,
            outbox,
            unidadDeTrabajo,
            Options.Create(new InventarioOptions()),
            NullLogger<ServicioExpiracionReservas>.Instance);

        return (servicio, repoReservas, outbox, unidadDeTrabajo);
    }

    [Fact]
    public async Task Reserva_vencida_devuelve_al_disponible_exactamente_lo_reservado_y_queda_expirada()
    {
        var existencia = ExistenciaReservada(fisica: 10, aReservar: 3);
        var (servicio, reservas, _, unidadDeTrabajo) = Construir(
            new RepositorioExistenciasFalso().Precargar(existencia),
            ReservaVencida("RSV-2026-000001", "ORD-2026-000001"));

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(1, expiradas);
        Assert.Equal(EstadoReserva.EXPIRADA, reservas.Todas.Single().Estado);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Equal(10, existencia.Disponible);
        Assert.Equal(10, existencia.CantidadFisica);
        Assert.Equal(1, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Reserva_con_TTL_vigente_no_se_toca()
    {
        var existencia = ExistenciaReservada(fisica: 10, aReservar: 3);
        var vigente = Reserva.Crear(
            "RSV-2026-000002",
            "ORD-2026-000002",
            Ubicacion,
            TipoUbicacion.CENTRO_DISTRIBUCION,
            900,
            new[] { (Sku, 3) },
            DateTime.UtcNow);

        var (servicio, reservas, outbox, unidadDeTrabajo) = Construir(
            new RepositorioExistenciasFalso().Precargar(existencia),
            vigente);

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(0, expiradas);
        Assert.Equal(EstadoReserva.ACTIVA, reservas.Todas.Single().Estado);
        Assert.Equal(3, existencia.CantidadReservada);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Reserva_ya_expirada_no_se_reprocesa()
    {
        var existencia = ExistenciaReservada(fisica: 10, aReservar: 3);
        var yaExpirada = ReservaVencida("RSV-2026-000003", "ORD-2026-000003");
        yaExpirada.MarcarExpirada();

        var (servicio, _, outbox, unidadDeTrabajo) = Construir(
            new RepositorioExistenciasFalso().Precargar(existencia),
            yaExpirada);

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(0, expiradas);
        // La liberacion no se aplica dos veces: el disponible sigue como estaba.
        Assert.Equal(3, existencia.CantidadReservada);
        Assert.Empty(outbox.Eventos);
        Assert.Equal(0, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Si_una_reserva_del_lote_falla_las_demas_si_se_expiran()
    {
        var primera = ExistenciaReservada(fisica: 10, aReservar: 3, sku: "SKU-A");
        var segunda = ExistenciaReservada(fisica: 8, aReservar: 2, sku: "SKU-B");

        var (servicio, reservas, outbox, unidadDeTrabajo) = Construir(
            new RepositorioExistenciasFalso().Precargar(primera, segunda),
            ReservaVencida("RSV-2026-000004", "ORD-2026-000004", cantidad: 3, sku: "SKU-A"),
            ReservaVencida("RSV-2026-000005", "ORD-2026-000005", cantidad: 2, sku: "SKU-B"));

        // La primera confirmacion choca con el bloqueo optimista; la segunda no.
        unidadDeTrabajo.LanzarConflictoEnIntento = 1;

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        // Lo que importa del criterio de aceptacion: el fallo de una reserva no
        // arrastra a las demas del lote, y de la que fallo no se cuenta ni se confirma
        // nada (se pidio el descarte, asi que sigue ACTIVA y vencida en la base y el
        // proximo barrido la vuelve a tomar). El estado en memoria de la fallida no se
        // asserta: el doble comparte la instancia y no puede deshacer la mutacion como
        // si hace ChangeTracker.Clear() sobre la fila real.
        Assert.Equal(1, expiradas);
        Assert.Equal(1, unidadDeTrabajo.Descartes);
        Assert.Equal(1, unidadDeTrabajo.Confirmaciones);

        var exitosa = reservas.Todas.Single(reserva => reserva.Id == "RSV-2026-000005");
        Assert.Equal(EstadoReserva.EXPIRADA, exitosa.Estado);
        Assert.Equal(0, segunda.CantidadReservada);
        Assert.Equal(8, segunda.Disponible);

    }

    [Fact]
    public async Task El_evento_encolado_viaja_bajo_la_reserva_con_sequence_dos()
    {
        var existencia = ExistenciaReservada(fisica: 10, aReservar: 3);
        var (servicio, _, outbox, _) = Construir(
            new RepositorioExistenciasFalso().Precargar(existencia),
            ReservaVencida("RSV-2026-000006", "ORD-2026-000006"));

        await servicio.ProcesarLoteAsync(CancellationToken.None);

        var evento = Assert.Single(outbox.PorTipo(ReservaInventarioExpirada.EventType));
        Assert.Equal(ReservaInventarioExpirada.AggregateType, evento.AggregateType);
        Assert.Equal("RSV-2026-000006", evento.AggregateId);
        Assert.Equal(ReservaInventarioExpirada.RoutingKey, evento.RoutingKey);

        // 1 la consumio InventarioReservado al crearse la reserva.
        Assert.Equal(2, evento.Sequence);

        var data = Assert.IsType<ReservaInventarioExpiradaData>(evento.Data);
        Assert.Equal("ORD-2026-000006", data.OrdenId);
        Assert.Equal(Ubicacion, data.UbicacionId);
        Assert.Equal(Sku, Assert.Single(data.Lineas).Sku);
        Assert.Equal(3, Assert.Single(data.Lineas).Cantidad);
    }

    [Fact]
    public async Task Reserva_de_varias_lineas_libera_cada_SKU_en_su_existencia()
    {
        var deA = ExistenciaReservada(fisica: 10, aReservar: 4, sku: "SKU-A");
        var deB = ExistenciaReservada(fisica: 6, aReservar: 1, sku: "SKU-B");

        var reserva = Reserva.Crear(
            "RSV-2026-000007",
            "ORD-2026-000007",
            Ubicacion,
            TipoUbicacion.CENTRO_DISTRIBUCION,
            60,
            new[] { ("SKU-A", 4), ("SKU-B", 1) },
            DateTime.UtcNow.AddMinutes(-10));

        var (servicio, _, _, _) = Construir(
            new RepositorioExistenciasFalso().Precargar(deA, deB),
            reserva);

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(1, expiradas);
        Assert.Equal(0, deA.CantidadReservada);
        Assert.Equal(0, deB.CantidadReservada);
    }
}
