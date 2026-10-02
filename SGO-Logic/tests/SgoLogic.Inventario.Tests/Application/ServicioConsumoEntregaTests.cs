using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>
/// Salida fisica al enviar el pedido a ruta: solo sobre reservas que retienen
/// stock, exactamente lo reservado y nunca dos veces.
/// </summary>
public sealed class ServicioConsumoEntregaTests
{
    private const string Sku = "PROD-002";
    private const string Ubicacion = "TIENDA-01-CENTRAL";
    private const string OrdenId = "ORD-2026-000011";

    private static (ServicioConsumoEntrega Servicio,
        RepositorioExistenciasFalso Existencias,
        RepositorioMovimientosFalso Movimientos) Construir(Existencia? existencia = null, Reserva? reserva = null)
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

        var movimientos = new RepositorioMovimientosFalso();

        return (new ServicioConsumoEntrega(
                reservas, existencias, movimientos, NullLogger<ServicioConsumoEntrega>.Instance),
            existencias, movimientos);
    }

    private static Existencia CrearExistencia(int fisica, int reservada)
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, fisica);
        if (reservada > 0)
        {
            existencia.Reservar(reservada);
        }
        return existencia;
    }

    private static Reserva CrearReserva(int cantidad = 33) =>
        Reserva.Crear(
            "RSV-2026-000011",
            OrdenId,
            Ubicacion,
            TipoUbicacion.TIENDA,
            900,
            new[] { (Sku, cantidad) },
            DateTime.UtcNow);

    [Fact]
    public async Task Consumir_reserva_confirmada_descuenta_exactamente_lo_reservado()
    {
        // 50 fisica, 33 de esta orden + 5 de otra reservadas => 12 disponibles.
        var existencia = CrearExistencia(fisica: 50, reservada: 38);
        var reserva = CrearReserva(33);
        reserva.Confirmar();

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        var resultado = await servicio.ConsumirAsync(OrdenId, CancellationToken.None);

        Assert.True(resultado);
        Assert.Equal(17, existencia.CantidadFisica);
        // Las 5 unidades de la otra orden siguen reservadas.
        Assert.Equal(5, existencia.CantidadReservada);
        Assert.Equal(12, existencia.Disponible);
        Assert.Equal(EstadoReserva.ENTREGADA, reserva.Estado);

        var movimiento = Assert.Single(movimientos.Todos);
        Assert.Equal(TipoMovimiento.SALIDA, movimiento.Tipo);
        Assert.Equal(33, movimiento.Cantidad);
        Assert.Equal(17, movimiento.CantidadFisicaResultante);
        Assert.Equal(MotivoMovimiento.DESPACHO, movimiento.Motivo);
    }

    [Fact]
    public async Task Consumir_reserva_activa_tambien_descuenta()
    {
        var existencia = CrearExistencia(fisica: 10, reservada: 4);
        var reserva = CrearReserva(4);

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        Assert.True(await servicio.ConsumirAsync(OrdenId, CancellationToken.None));

        Assert.Equal(6, existencia.CantidadFisica);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Equal(EstadoReserva.ENTREGADA, reserva.Estado);
        Assert.Single(movimientos.Todos);
    }

    [Fact]
    public async Task Consumo_duplicado_no_descuenta_dos_veces()
    {
        var existencia = CrearExistencia(fisica: 50, reservada: 33);
        var reserva = CrearReserva(33);
        reserva.Confirmar();

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        Assert.True(await servicio.ConsumirAsync(OrdenId, CancellationToken.None));
        Assert.True(await servicio.ConsumirAsync(OrdenId, CancellationToken.None));

        Assert.Equal(17, existencia.CantidadFisica);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Single(movimientos.Todos);
    }

    [Fact]
    public async Task Consumo_sobre_reserva_liberada_no_descuenta()
    {
        var existencia = CrearExistencia(fisica: 50, reservada: 5);
        var reserva = CrearReserva(33);
        reserva.Liberar();

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        var resultado = await servicio.ConsumirAsync(OrdenId, CancellationToken.None);

        // Exito para que el mensaje no se reintente, pero sin tocar nada.
        Assert.True(resultado);
        Assert.Equal(50, existencia.CantidadFisica);
        Assert.Equal(5, existencia.CantidadReservada);
        Assert.Equal(EstadoReserva.LIBERADA, reserva.Estado);
        Assert.Empty(movimientos.Todos);
    }

    [Fact]
    public async Task Consumo_sobre_reserva_expirada_no_descuenta()
    {
        var existencia = CrearExistencia(fisica: 50, reservada: 0);
        var reserva = CrearReserva(33);
        reserva.MarcarExpirada();

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        Assert.True(await servicio.ConsumirAsync(OrdenId, CancellationToken.None));

        Assert.Equal(50, existencia.CantidadFisica);
        Assert.Equal(EstadoReserva.EXPIRADA, reserva.Estado);
        Assert.Empty(movimientos.Todos);
    }

    [Fact]
    public async Task Consumo_con_reservada_menor_a_la_linea_lanza_sin_tocar_nada()
    {
        // La existencia no tiene las 33 reservadas: consumir liberaria unidades ajenas.
        var existencia = CrearExistencia(fisica: 50, reservada: 10);
        var reserva = CrearReserva(33);

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        await Assert.ThrowsAsync<ExcepcionConsumoReservaInconsistente>(
            () => servicio.ConsumirAsync(OrdenId, CancellationToken.None));

        Assert.Equal(50, existencia.CantidadFisica);
        Assert.Equal(10, existencia.CantidadReservada);
        Assert.Equal(EstadoReserva.ACTIVA, reserva.Estado);
        Assert.Empty(movimientos.Todos);
    }

    [Fact]
    public async Task Consumir_es_idempotente_si_ya_estaba_entregada()
    {
        var existencia = CrearExistencia(fisica: 17, reservada: 0);
        var reserva = CrearReserva(33);
        reserva.MarcarEntregada();

        var (servicio, _, movimientos) = Construir(existencia, reserva);

        Assert.True(await servicio.ConsumirAsync(OrdenId, CancellationToken.None));

        Assert.Equal(17, existencia.CantidadFisica);
        Assert.Empty(movimientos.Todos);
    }

    [Fact]
    public async Task Consumir_retorna_false_si_reserva_no_existe()
    {
        var (servicio, _, _) = Construir();

        var resultado = await servicio.ConsumirAsync("ORD-INEXISTENTE", CancellationToken.None);

        Assert.False(resultado);
    }
}
