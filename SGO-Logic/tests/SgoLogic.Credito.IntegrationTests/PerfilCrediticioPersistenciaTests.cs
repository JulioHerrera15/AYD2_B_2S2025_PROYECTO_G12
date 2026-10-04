using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Credito contra PostgreSQL real. Cada operacion corre en su propio alcance (un
/// CreditoDbContext nuevo) para que las lecturas viajen a la base y no al change
/// tracker. Cada prueba usa un cliente propio: el contenedor es compartido.
/// </summary>
[Collection("credito-integracion")]
public sealed class PerfilCrediticioPersistenciaTests
{
    private readonly CreditoFixture _fixture;

    public PerfilCrediticioPersistenciaTests(CreditoFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ClienteNuevo() => $"cliente-{Guid.NewGuid():N}@sgo.local";

    /// <summary>Historial del cliente1 de demo: 98, EXCELENTE, 7500.</summary>
    private static HistorialClienteErpActualizadoData Excelente(string clienteId, DateTime fechaCorte) =>
        new(clienteId, "ERP-INT-1", fechaCorte.AddDays(-1100), 18, 14500m, "GTQ", 18, 0, 0, 1, 0, 0, fechaCorte, "LOTE-INT");

    private static async Task<ResultadoAplicacionHistorial> AplicarAsync(ServiceProvider proveedor, HistorialClienteErpActualizadoData datos)
    {
        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>();
        return await servicio.AplicarHistorialAsync(datos, CancellationToken.None);
    }

    [Fact]
    public async Task La_migracion_crea_el_esquema_credito_y_el_outbox_en_public()
    {
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var conexion = contexto.Database.GetDbConnection();
        await conexion.OpenAsync();

        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
            "WHERE table_schema IN ('credito', 'public') ORDER BY 1";

        var tablas = new List<string>();
        await using (var lector = await comando.ExecuteReaderAsync())
        {
            while (await lector.ReadAsync())
            {
                tablas.Add(lector.GetString(0));
            }
        }

        Assert.Contains("credito.historial_cliente", tablas);
        Assert.Contains("credito.perfil_crediticio", tablas);
        Assert.Contains("credito.linea_credito", tablas);
        Assert.Contains("credito.mensajes_procesados", tablas);
        Assert.Contains("public.outbox_messages", tablas);

        var migraciones = await contexto.Database.GetAppliedMigrationsAsync();
        Assert.Contains(migraciones, m => m.EndsWith("_Inicial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Aplicar_un_historial_persiste_historial_perfil_y_linea()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        var corte = DateTime.UtcNow;

        var resultado = await AplicarAsync(proveedor, Excelente(clienteId, corte));

        Assert.True(resultado.Aplicado);
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);

        var historial = await contexto.Historiales.AsNoTracking().SingleAsync(h => h.ClienteId == clienteId);
        Assert.Equal(18, historial.ComprasUltimos12Meses);
        Assert.Equal(14500m, historial.MontoCompradoUltimos12Meses);
        Assert.Equal(DateTimeKind.Utc, historial.FechaCorte.Kind);

        var perfil = await contexto.Perfiles.AsNoTracking().SingleAsync(p => p.ClienteId == clienteId);
        Assert.Equal(98, perfil.Puntaje);
        Assert.Equal(NivelCredito.EXCELENTE, perfil.Nivel);
        Assert.True(perfil.Elegible);
        Assert.Equal(40m, perfil.PuntosPuntualidad);
        Assert.Equal(8.15m, perfil.PuntosDevoluciones);
        Assert.Equal("2026.10-v2", perfil.VersionPolitica);

        var linea = await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId);
        Assert.Equal(EstadoLineaCredito.ACTIVA, linea.Estado);
        Assert.Equal(7500m, linea.LimiteAprobado);
        Assert.Equal(0m, linea.SaldoUtilizado);
    }

    [Fact]
    public async Task El_evento_queda_en_outbox_messages_en_la_misma_transaccion()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();

        await AplicarAsync(proveedor, Excelente(clienteId, DateTime.UtcNow));

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var mensaje = await contexto.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == clienteId);

        Assert.Equal(PerfilCrediticioActualizado.EventType, mensaje.EventType);
        Assert.Equal(PerfilCrediticioActualizado.RoutingKey, mensaje.RoutingKey);
        Assert.Equal(PerfilCrediticioActualizado.AggregateType, mensaje.AggregateType);
        Assert.Contains("\"source\":\"credito\"", mensaje.Payload);
        Assert.Contains("\"nivel\":\"EXCELENTE\"", mensaje.Payload);
        Assert.Contains("\"limiteAprobado\":\"7500.00\"", mensaje.Payload);
    }

    [Fact]
    public async Task Aplicar_dos_veces_no_duplica_ni_reemite()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        // Fraccion menor a un microsegundo distinta de cero: PostgreSQL la trunca al
        // guardar, asi que el segundo intento compara contra un valor releido menor.
        var corte = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(7);
        var datos = Excelente(clienteId, corte);

        await AplicarAsync(proveedor, datos);
        var segundo = await AplicarAsync(proveedor, datos);

        Assert.False(segundo.Aplicado);
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        Assert.Equal(1, await contexto.Historiales.CountAsync(h => h.ClienteId == clienteId));
        Assert.Equal(1, await contexto.Perfiles.CountAsync(p => p.ClienteId == clienteId));
        Assert.Equal(1, await contexto.Lineas.CountAsync(l => l.ClienteId == clienteId));
        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.AggregateId == clienteId));
        Assert.Equal(1, (await contexto.Perfiles.AsNoTracking().SingleAsync(p => p.ClienteId == clienteId)).Version);
    }

    [Fact]
    public async Task La_elegibilidad_lee_el_perfil_persistido()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        await AplicarAsync(proveedor, Excelente(clienteId, DateTime.UtcNow));

        using var alcance = proveedor.CreateScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioElegibilidad>();

        var oferta = await servicio.ConsultarAsync(clienteId.ToUpperInvariant(), 8000m, CancellationToken.None);
        Assert.Equal(EstadosOfertaCredito.CupoInsuficiente, oferta.Estado);
        Assert.Equal(7500m, oferta.Disponible);

        var credito = await servicio.ObtenerMiCreditoAsync(clienteId, CancellationToken.None);
        Assert.Equal(EstadosOfertaCredito.Disponible, credito.Estado);
        Assert.Equal("Excelente", credito.EtiquetaNivel);
        Assert.Equal(7500m, credito.LimiteAprobado);
        Assert.Equal(4, credito.Factores.Count);

        var sinHistorial = await servicio.ConsultarAsync(ClienteNuevo(), null, CancellationToken.None);
        Assert.Equal(EstadosOfertaCredito.SinHistorial, sinHistorial.Estado);
    }

    [Fact]
    public async Task Dos_recalculos_concurrentes_del_mismo_perfil_chocan_por_version()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        await AplicarAsync(proveedor, Excelente(clienteId, DateTime.UtcNow));

        await using var primero = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        await using var segundo = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var perfilA = await primero.Perfiles.SingleAsync(p => p.ClienteId == clienteId);
        var perfilB = await segundo.Perfiles.SingleAsync(p => p.ClienteId == clienteId);

        var politica = new PoliticaCredito(new PoliticaCreditoOptions());
        var evaluacion = politica.Evaluar(null, DateTime.UtcNow);
        perfilA.Recalcular(evaluacion, DateTime.UtcNow, DateTime.UtcNow);
        perfilB.Recalcular(evaluacion, DateTime.UtcNow, DateTime.UtcNow);

        await primero.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
    }
}
