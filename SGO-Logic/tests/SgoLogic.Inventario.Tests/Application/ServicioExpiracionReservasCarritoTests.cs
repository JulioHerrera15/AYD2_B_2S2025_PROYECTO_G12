using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Inventario.Tests.Application;

/// <summary>Las retenciones de carrito con TTL vencido devuelven sus unidades al disponible.</summary>
public sealed class ServicioExpiracionReservasCarritoTests
{
    private const string Carrito = "guest:3b0d9a51c2e64b5c8a1f7e2d4c6b8a90";
    private const string Sku = "PROD-105";
    private const string Ubicacion = "TIENDA-01-CENTRAL";

    private static ReservaCarrito Retencion(Existencia existencia, int cantidad, int minutosAtras)
    {
        existencia.Reservar(cantidad);
        return ReservaCarrito.Crear(
            Carrito,
            existencia.Sku,
            existencia.UbicacionId,
            existencia.TipoUbicacion,
            cantidad,
            existencia.UbicacionId,
            900,
            DateTime.UtcNow.AddMinutes(-minutosAtras));
    }

    private static (ServicioExpiracionReservasCarrito Servicio, UnidadDeTrabajoFalsa UnidadDeTrabajo) Construir(
        Existencia existencia,
        params ReservaCarrito[] retenciones)
    {
        var unidadDeTrabajo = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioExpiracionReservasCarrito(
            new RepositorioReservasCarritoFalso().Precargar(retenciones),
            new RepositorioExistenciasFalso().Precargar(existencia),
            unidadDeTrabajo,
            Options.Create(new InventarioOptions()),
            NullLogger<ServicioExpiracionReservasCarrito>.Instance);

        return (servicio, unidadDeTrabajo);
    }

    [Fact]
    public async Task Expirar_una_retencion_vencida_libera_la_reservada_y_la_marca_expirada()
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 2);
        var vencida = Retencion(existencia, 2, minutosAtras: 20);
        Assert.Equal(0, existencia.Disponible);

        var (servicio, unidadDeTrabajo) = Construir(existencia, vencida);

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(1, expiradas);
        Assert.Equal(EstadoReservaCarrito.EXPIRADA, vencida.Estado);
        Assert.Equal(0, existencia.CantidadReservada);
        Assert.Equal(2, existencia.Disponible);
        Assert.Equal(1, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Una_retencion_aun_vigente_no_se_toca()
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 2);
        var vigente = Retencion(existencia, 1, minutosAtras: 1);

        var (servicio, unidadDeTrabajo) = Construir(existencia, vigente);

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(0, expiradas);
        Assert.Equal(EstadoReservaCarrito.ACTIVA, vigente.Estado);
        Assert.Equal(1, existencia.CantidadReservada);
        Assert.Equal(0, unidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Si_falla_la_confirmacion_descarta_y_la_retencion_queda_para_el_proximo_barrido()
    {
        var existencia = new Existencia(Sku, Ubicacion, TipoUbicacion.TIENDA, 2);
        var vencida = Retencion(existencia, 1, minutosAtras: 30);

        var (servicio, unidadDeTrabajo) = Construir(existencia, vencida);
        unidadDeTrabajo.LanzarConflicto = true;

        var expiradas = await servicio.ProcesarLoteAsync(CancellationToken.None);

        Assert.Equal(0, expiradas);
        Assert.Equal(1, unidadDeTrabajo.Descartes);
    }
}
