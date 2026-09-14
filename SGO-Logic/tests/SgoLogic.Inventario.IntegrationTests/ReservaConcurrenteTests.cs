using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;

namespace SgoLogic.Inventario.IntegrationTests;

/// <summary>
/// HU-026 (EAC-05): ninguna unidad se compromete a dos clientes a la vez, ni
/// siquiera con alta concurrencia. No hay logica de produccion nueva detras de
/// esta historia — el bloqueo optimista de ADR-005 ya lo garantiza por diseño —;
/// lo que falta y aporta esta suite es la evidencia reproducible que el DoD exige.
///
/// La prueba se apoya en dos angulos que se complementan:
/// un caso DETERMINISTA, que demuestra que el token de concurrencia detecta el
/// choque sin depender de ninguna carrera, y casos PROBABILISTICOS con N tareas
/// en paralelo, que demuestran que el resultado agregado nunca sobrevende.
/// </summary>
[Collection("inventario-integracion")]
public sealed class ReservaConcurrenteTests
{
    private readonly InventarioFixture _fixture;

    public ReservaConcurrenteTests(InventarioFixture fixture)
    {
        _fixture = fixture;
    }

    private static string SkuNuevo() => $"SKU-CC-{Guid.NewGuid():N}"[..20];

    private async Task CargarExistenciaAsync(ServiceProvider proveedor, string sku, string ubicacion, int cantidad)
    {
        using var alcance = proveedor.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<ServicioExistencias>().RegistrarAsync(
            new RegistrarExistenciaRequest(sku, ubicacion, TipoUbicacion.CENTRO_DISTRIBUCION, cantidad),
            CancellationToken.None);
    }

    /// <summary>
    /// Un intento de reserva como lo haria Ordenes: alcance propio (o sea, conexion
    /// y DbContext propios, que es lo que hace real la concurrencia) y reintento
    /// ante choque de version, porque un 409 de concurrencia no significa que no
    /// haya stock sino que alguien mas llego primero a escribir.
    /// </summary>
    private static async Task<ResultadoIntento> IntentarReservarAsync(
        ServiceProvider proveedor,
        string ordenId,
        string sku,
        string ubicacion,
        int cantidad,
        int reintentosMaximos = 25)
    {
        for (var intento = 0; intento <= reintentosMaximos; intento++)
        {
            using var alcance = proveedor.CreateScope();
            var servicio = alcance.ServiceProvider.GetRequiredService<ServicioReservas>();
            var unidadDeTrabajo = alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>();

            try
            {
                var resultado = await servicio.ReservarAsync(
                    new SolicitudReserva(
                        ordenId,
                        MetodoEntrega.RECOGIDA_TIENDA,
                        ubicacion,
                        900,
                        new[] { new LineaSolicitudReserva(sku, cantidad) }),
                    CancellationToken.None);

                if (resultado.Rechazada)
                {
                    return ResultadoIntento.SinStock;
                }

                await unidadDeTrabajo.GuardarCambiosAsync(CancellationToken.None);
                return ResultadoIntento.Reservada;
            }
            catch (ExcepcionConflictoConcurrencia)
            {
                // Otro intento gano la carrera de escritura. Se vuelve a leer el
                // estado actual y se decide de nuevo: puede que ahora falte stock.
            }
        }

        return ResultadoIntento.AgotoReintentos;
    }

    private enum ResultadoIntento
    {
        Reservada,
        SinStock,
        AgotoReintentos
    }

    [Fact]
    public async Task Una_sola_unidad_disputada_por_muchas_ordenes_produce_una_sola_reserva()
    {
        // El criterio literal de EAC-05: N solicitudes simultaneas sobre la misma
        // unidad resultan en una sola reserva exitosa.
        const int solicitudes = 20;

        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        await CargarExistenciaAsync(proveedor, sku, "CD-CC-01", 1);

        var resultados = await Task.WhenAll(Enumerable.Range(0, solicitudes).Select(indice =>
            IntentarReservarAsync(proveedor, $"ORD-CC1-{Guid.NewGuid():N}"[..20], sku, "CD-CC-01", 1)));

        Assert.Equal(1, resultados.Count(r => r == ResultadoIntento.Reservada));
        Assert.Equal(solicitudes - 1, resultados.Count(r => r == ResultadoIntento.SinStock));

        // Ningun intento debe rendirse por agotar reintentos: eso seria contencion
        // sin resolver, no una decision de negocio.
        Assert.DoesNotContain(ResultadoIntento.AgotoReintentos, resultados);

        await using var contexto = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);
        var existencia = await contexto.Existencias.SingleAsync(e => e.Sku == sku);

        Assert.Equal(1, existencia.CantidadReservada);
        Assert.Equal(0, existencia.CantidadFisica - existencia.CantidadReservada);
    }

    [Fact]
    public async Task Con_mas_solicitudes_que_stock_se_reserva_exactamente_el_stock_y_nunca_mas()
    {
        // El angulo de no-sobreventa con volumen: 50 solicitudes sobre 10 unidades.
        const int solicitudes = 50;
        const int stock = 10;

        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        await CargarExistenciaAsync(proveedor, sku, "CD-CC-02", stock);

        var resultados = await Task.WhenAll(Enumerable.Range(0, solicitudes).Select(indice =>
            IntentarReservarAsync(proveedor, $"ORD-CC2-{Guid.NewGuid():N}"[..20], sku, "CD-CC-02", 1)));

        Assert.Equal(stock, resultados.Count(r => r == ResultadoIntento.Reservada));
        Assert.Equal(solicitudes - stock, resultados.Count(r => r == ResultadoIntento.SinStock));
        Assert.DoesNotContain(ResultadoIntento.AgotoReintentos, resultados);

        await using var contexto = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);
        var existencia = await contexto.Existencias.SingleAsync(e => e.Sku == sku);

        Assert.Equal(stock, existencia.CantidadReservada);
        Assert.Equal(stock, existencia.CantidadFisica);

        // La propiedad que de verdad importa: el disponible toca cero pero nunca baja de ahi.
        Assert.Equal(0, existencia.CantidadFisica - existencia.CantidadReservada);

        // Y hay tantas reservas persistidas como unidades habia: ni una de mas.
        var reservas = await contexto.Reservas.CountAsync(r => r.Lineas.Any(l => l.Sku == sku));
        Assert.Equal(stock, reservas);
    }

    [Fact]
    public async Task Cada_reserva_exitosa_emite_exactamente_un_evento()
    {
        // Los reintentos no deben dejar eventos duplicados en el outbox: el evento
        // se encola en la misma transaccion que la reserva, asi que un intento que
        // choca se descarta entero, evento incluido.
        const int solicitudes = 30;
        const int stock = 6;

        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        await CargarExistenciaAsync(proveedor, sku, "CD-CC-03", stock);

        await Task.WhenAll(Enumerable.Range(0, solicitudes).Select(indice =>
            IntentarReservarAsync(proveedor, $"ORD-CC3-{Guid.NewGuid():N}"[..20], sku, "CD-CC-03", 1)));

        await using var contexto = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);

        var idsReserva = await contexto.Reservas
            .Where(r => r.Lineas.Any(l => l.Sku == sku))
            .Select(r => r.Id)
            .ToListAsync();

        Assert.Equal(stock, idsReserva.Count);

        var eventos = await contexto.OutboxMessages
            .Where(m => m.EventType == "InventarioReservado" && idsReserva.Contains(m.AggregateId))
            .ToListAsync();

        Assert.Equal(stock, eventos.Count);
        Assert.Equal(idsReserva.Count, eventos.Select(e => e.AggregateId).Distinct().Count());
    }

    [Fact]
    public async Task El_token_de_concurrencia_rechaza_la_escritura_que_llega_con_version_vieja()
    {
        // Caso DETERMINISTA, sin carrera: dos contextos leen la misma existencia en
        // la version N; el primero escribe y la deja en N+1; el segundo escribe con
        // N en el WHERE, no afecta ninguna fila y EF Core lo reporta como conflicto.
        // Esto es lo que impide la actualizacion perdida que causaria sobreventa.
        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        await CargarExistenciaAsync(proveedor, sku, "CD-CC-04", 5);

        await using var primero = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);
        await using var segundo = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);

        var vistaPrimero = await primero.Existencias.SingleAsync(e => e.Sku == sku);
        var vistaSegundo = await segundo.Existencias.SingleAsync(e => e.Sku == sku);

        Assert.Equal(vistaPrimero.Version, vistaSegundo.Version);

        vistaPrimero.Reservar(3);
        await primero.SaveChangesAsync();

        vistaSegundo.Reservar(3);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());

        // Solo la primera escritura quedo: 3 reservadas, no 6 (que habria sobrevendido).
        await using var verificacion = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);
        var final = await verificacion.Existencias.SingleAsync(e => e.Sku == sku);

        Assert.Equal(3, final.CantidadReservada);
        Assert.Equal(2, final.CantidadFisica - final.CantidadReservada);
    }

    [Fact]
    public async Task La_misma_orden_pedida_en_paralelo_no_duplica_la_reserva()
    {
        // Idempotencia bajo concurrencia: si Ordenes reintenta el mensaje mientras el
        // primero aun no confirma, el indice unico sobre orden_id impide la segunda
        // reserva. La orden termina con una sola, pase lo que pase.
        const int intentos = 10;

        await using var proveedor = InventarioServicios.CrearProveedor(_fixture.CadenaConexion);
        var sku = SkuNuevo();
        var ordenId = $"ORD-CC5-{Guid.NewGuid():N}"[..20];
        await CargarExistenciaAsync(proveedor, sku, "CD-CC-05", 20);

        // Se toleran los fallos de escritura: lo que se afirma no es que todos los
        // intentos tengan exito, sino que la base nunca quede con dos reservas.
        await Task.WhenAll(Enumerable.Range(0, intentos).Select(async indice =>
        {
            try
            {
                await IntentarReservarAsync(proveedor, ordenId, sku, "CD-CC-05", 2);
            }
            catch (DbUpdateException)
            {
                // Violacion del indice unico sobre orden_id: exactamente la defensa
                // que se esta probando.
            }
        }));

        await using var contexto = InventarioServicios.CrearDbContext(_fixture.CadenaConexion);

        Assert.Equal(1, await contexto.Reservas.CountAsync(r => r.OrdenId == ordenId));

        var existencia = await contexto.Existencias.SingleAsync(e => e.Sku == sku);
        Assert.Equal(2, existencia.CantidadReservada);
    }
}
