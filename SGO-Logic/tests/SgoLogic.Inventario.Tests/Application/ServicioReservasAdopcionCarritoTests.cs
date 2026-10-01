using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// Al reservar la orden de un carrito, sus retenciones se adoptan: las unidades
/// pasan de la retencion a la reserva de la orden sin contarse dos veces.
/// </summary>
public sealed class ServicioReservasAdopcionCarritoTests
{
    private const string OrdenId = "ORD-2026-000901";
    private const string Carrito = "cart:6f1c2a7e-0000-4000-8000-000000000002";
    private const string Sku = "PROD-105";
    private const string Cd = "CD-CENTRAL";

    private static (ServicioReservas Servicio, RepositorioReservasFalso Reservas, OutboxWriterFalso Outbox) Construir(
        RepositorioReservasCarritoFalso retenciones,
        params Existencia[] existencias)
    {
        var reservas = new RepositorioReservasFalso();
        var outbox = new OutboxWriterFalso();
        var servicio = new ServicioReservas(
            new RepositorioExistenciasFalso().Precargar(existencias),
            reservas,
            outbox,
            new GeneradorCorrelativoReservaFalso(901),
            retenciones,
            Options.Create(new InventarioOptions()));

        return (servicio, reservas, outbox);
    }

    private static ReservaCarrito RetenerEn(Existencia existencia, int cantidad, string carrito = Carrito)
    {
        existencia.Reservar(cantidad);
        return ReservaCarrito.Crear(carrito, existencia.Sku, existencia.UbicacionId, existencia.TipoUbicacion, cantidad, null, 900, DateTime.UtcNow);
    }

    private static SolicitudReserva Solicitud(int cantidad, string? carritoId) =>
        new(OrdenId, MetodoEntrega.DOMICILIO, null, 900, new[] { new LineaSolicitudReserva(Sku, cantidad) }, carritoId);

    [Fact]
    public async Task Adoptar_libera_las_retenciones_y_reserva_la_orden_sin_doble_conteo()
    {
        // Las 2 unidades del CD estan retenidas por el carrito: el disponible publico es 0.
        var cd = new Existencia(Sku, Cd, TipoUbicacion.CENTRO_DISTRIBUCION, 2);
        var retencion = RetenerEn(cd, 2);
        Assert.Equal(0, cd.Disponible);

        var (servicio, reservas, outbox) = Construir(new RepositorioReservasCarritoFalso().Precargar(retencion), cd);

        var resultado = await servicio.ReservarAsync(Solicitud(2, Carrito), CancellationToken.None);

        Assert.False(resultado.Rechazada);
        Assert.Equal(Cd, resultado.Reserva!.UbicacionId);
        Assert.Equal(EstadoReservaCarrito.ADOPTADA, retencion.Estado);

        // Las mismas 2 unidades: antes por la retencion, ahora por la reserva. Nunca 4.
        Assert.Equal(2, cd.CantidadReservada);
        Assert.Equal(2, cd.CantidadFisica);
        Assert.Equal(1, reservas.LlamadasAgregar);
        Assert.Single(outbox.PorTipo(InventarioReservado.EventType));
    }

    [Fact]
    public async Task Sin_CarritoId_las_unidades_retenidas_por_un_carrito_no_estan_disponibles()
    {
        var cd = new Existencia(Sku, Cd, TipoUbicacion.CENTRO_DISTRIBUCION, 2);
        var retencion = RetenerEn(cd, 2);

        var (servicio, _, _) = Construir(new RepositorioReservasCarritoFalso().Precargar(retencion), cd);

        var resultado = await servicio.ReservarAsync(Solicitud(2, carritoId: null), CancellationToken.None);

        Assert.True(resultado.Rechazada);
        Assert.Equal(EstadoReservaCarrito.ACTIVA, retencion.Estado);
        Assert.Equal(2, cd.CantidadReservada);
    }

    [Fact]
    public async Task Si_la_reserva_se_rechaza_las_retenciones_del_carrito_quedan_intactas()
    {
        var cd = new Existencia(Sku, Cd, TipoUbicacion.CENTRO_DISTRIBUCION, 2);
        var retencion = RetenerEn(cd, 2);

        var (servicio, reservas, outbox) = Construir(new RepositorioReservasCarritoFalso().Precargar(retencion), cd);

        var resultado = await servicio.ReservarAsync(Solicitud(3, Carrito), CancellationToken.None);

        Assert.True(resultado.Rechazada);
        Assert.Equal(EstadoReservaCarrito.ACTIVA, retencion.Estado);
        Assert.Equal(2, cd.CantidadReservada);
        Assert.Equal(0, reservas.LlamadasAgregar);
        Assert.Single(outbox.PorTipo(ReservaInventarioRechazada.EventType));
    }

    [Fact]
    public async Task La_idempotencia_por_orden_se_mantiene_y_no_vuelve_a_adoptar()
    {
        var cd = new Existencia(Sku, Cd, TipoUbicacion.CENTRO_DISTRIBUCION, 4);
        var retencion = RetenerEn(cd, 2);
        var (servicio, reservas, _) = Construir(new RepositorioReservasCarritoFalso().Precargar(retencion), cd);

        await servicio.ReservarAsync(Solicitud(2, Carrito), CancellationToken.None);
        var repetida = await servicio.ReservarAsync(Solicitud(2, Carrito), CancellationToken.None);

        Assert.True(repetida.YaExistia);
        Assert.Equal(1, reservas.LlamadasAgregar);
        Assert.Equal(2, cd.CantidadReservada);
    }
}
