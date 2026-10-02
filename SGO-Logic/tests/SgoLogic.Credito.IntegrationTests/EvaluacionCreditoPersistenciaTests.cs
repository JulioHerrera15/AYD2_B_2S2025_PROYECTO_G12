using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Evaluacion y anulacion de compras a credito contra PostgreSQL real. Aqui si hay
/// concurrencia de verdad: varias evaluaciones simultaneas compiten por la Version de
/// la misma linea y entre todas no pueden superar el limite.
/// </summary>
[Collection("credito-integracion")]
public sealed class EvaluacionCreditoPersistenciaTests
{
    private readonly CreditoFixture _fixture;

    public EvaluacionCreditoPersistenciaTests(CreditoFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ClienteNuevo() => $"cliente-{Guid.NewGuid():N}@sgo.local";

    private static string OrdenNueva() => $"ORD-2026-{Random.Shared.Next(100000, 999999)}{Guid.NewGuid():N}"[..20];

    /// <summary>Historial del cliente3 de demo: 62, REGULAR, limite 1500.</summary>
    private static HistorialClienteErpActualizadoData Regular(string clienteId) =>
        new(clienteId, "ERP-INT-3", DateTime.UtcNow.AddDays(-300), 5, 6000m, "GTQ", 4, 1, 12, 1, 0, 0, DateTime.UtcNow, "LOTE-INT");

    private static async Task SembrarAsync(ServiceProvider proveedor, HistorialClienteErpActualizadoData historial)
    {
        using var alcance = proveedor.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>()
            .AplicarHistorialAsync(historial, CancellationToken.None);
    }

    private static async Task<ResultadoEvaluacion> EvaluarAsync(ServiceProvider proveedor, string ordenId, string clienteId, decimal monto)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<ServicioEvaluacionCredito>().EvaluarAsync(
            new EvaluacionCreditoSolicitadaData(ordenId, ordenId[^6..], clienteId, Canal.WEB, Moneda.GTQ, monto, DateTime.UtcNow),
            CancellationToken.None);
    }

    [Fact]
    public async Task Aprobar_persiste_cuenta_decision_saldo_y_evento_en_una_transaccion()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        var ordenId = OrdenNueva();
        await SembrarAsync(proveedor, Regular(clienteId));

        var resultado = await EvaluarAsync(proveedor, ordenId, clienteId, 1000m);

        Assert.Equal(ResultadoEvaluacion.Aprobada, resultado);
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);

        var cuenta = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.OrdenId == ordenId);
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, cuenta.Estado);
        Assert.Equal(1000m, cuenta.SaldoPendiente);
        Assert.Equal(30, (int)Math.Round((cuenta.VenceEn - cuenta.EmitidaEn).TotalDays));

        var decision = await contexto.Decisiones.AsNoTracking().SingleAsync(d => d.OrdenId == ordenId);
        Assert.Equal(ResultadoDecisionCredito.APROBADA, decision.Resultado);
        Assert.Equal(1500m, decision.DisponibleAntes);
        Assert.Equal(62, decision.Puntaje);

        var linea = await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId);
        Assert.Equal(1000m, linea.SaldoUtilizado);

        var mensaje = await contexto.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == ordenId);
        Assert.Equal(CreditoAprobado.EventType, mensaje.EventType);
        Assert.Contains("\"monto\":\"1000.00\"", mensaje.Payload);
    }

    [Fact]
    public async Task Evaluaciones_concurrentes_del_mismo_cliente_no_superan_el_limite()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        await SembrarAsync(proveedor, Regular(clienteId));

        // Limite 1500: de cuatro compras de 600 lanzadas a la vez solo caben dos.
        var ordenes = Enumerable.Range(0, 4).Select(_ => OrdenNueva()).ToList();
        var resultados = await Task.WhenAll(ordenes.Select(ordenId => EvaluarAsync(proveedor, ordenId, clienteId, 600m)));

        Assert.Equal(2, resultados.Count(r => r == ResultadoEvaluacion.Aprobada));
        Assert.Equal(2, resultados.Count(r => r == ResultadoEvaluacion.Rechazada));

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var linea = await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId);
        Assert.Equal(1200m, linea.SaldoUtilizado);
        Assert.True(linea.SaldoUtilizado <= linea.LimiteAprobado);
        Assert.Equal(2, await contexto.CuentasPorCobrar.CountAsync(c => c.ClienteId == clienteId));
        Assert.Equal(4, await contexto.Decisiones.CountAsync(d => d.ClienteId == clienteId));
        Assert.Equal(2, await contexto.Decisiones.CountAsync(d => d.ClienteId == clienteId && d.CodigoMotivo == CodigosMotivoCredito.CupoInsuficiente));
    }

    [Fact]
    public async Task Anular_devuelve_el_cupo_y_deja_la_cuenta_ANULADA()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        var ordenId = OrdenNueva();
        await SembrarAsync(proveedor, Regular(clienteId));
        await EvaluarAsync(proveedor, ordenId, clienteId, 900m);

        using (var alcance = proveedor.CreateScope())
        {
            var resultado = await alcance.ServiceProvider.GetRequiredService<ServicioAnulacionCargos>().ProcesarCancelacionAsync(
                new OrdenCanceladaData(ordenId, ordenId[^6..], clienteId, null, true, MotivoCancelacion.CLIENTE, "Cancelada",
                    false, null, null, null, null, DateTime.UtcNow, MedioPago.CREDITO),
                CancellationToken.None);
            Assert.Equal(ResultadoAnulacion.Anulada, resultado);
        }

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuenta = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.OrdenId == ordenId);
        Assert.Equal(EstadoCuentaPorCobrar.ANULADA, cuenta.Estado);
        Assert.NotNull(cuenta.AnuladaEn);
        Assert.Equal(0m, (await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).SaldoUtilizado);
        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.AggregateId == ordenId && m.EventType == CargoCreditoAnulado.EventType));
    }

    [Fact]
    public async Task Reinicio_de_demo_borra_cuentas_y_decisiones_sin_tocar_perfiles()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        await SembrarAsync(proveedor, Regular(clienteId));
        await EvaluarAsync(proveedor, OrdenNueva(), clienteId, 700m);

        using (var alcance = proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioDemoCredito>().ReiniciarAsync(CancellationToken.None);
        }

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        Assert.False(await contexto.CuentasPorCobrar.AnyAsync());
        Assert.False(await contexto.Decisiones.AnyAsync());
        Assert.False(await contexto.Lineas.AnyAsync(l => l.SaldoUtilizado != 0m));
        Assert.True(await contexto.Perfiles.AnyAsync(p => p.ClienteId == clienteId));
    }
}
