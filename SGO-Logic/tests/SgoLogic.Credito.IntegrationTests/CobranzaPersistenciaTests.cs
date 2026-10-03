using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Cobranza contra PostgreSQL real: abonos (secuencia de referencias e indice de abono
/// en curso), aplicacion del cobro en cuenta y linea, idempotencia y vencimiento.
/// </summary>
[Collection("credito-integracion")]
public sealed class CobranzaPersistenciaTests
{
    private readonly CreditoFixture _fixture;

    public CobranzaPersistenciaTests(CreditoFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ClienteNuevo() => $"cliente-{Guid.NewGuid():N}@sgo.local";

    private static string OrdenNueva() => $"ORD-2026-{Random.Shared.Next(100000, 999999)}{Guid.NewGuid():N}"[..20];

    /// <summary>Cliente EXCELENTE (7500) con una compra a credito de <paramref name="monto" />. Devuelve el id de la cuenta.</summary>
    private async Task<(string ClienteId, string OrdenId, Guid CuentaId)> CompraAsync(ServiceProvider proveedor, decimal monto)
    {
        var clienteId = ClienteNuevo();
        var ordenId = OrdenNueva();
        var corte = DateTime.UtcNow;

        using (var alcance = proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>().AplicarHistorialAsync(
                new HistorialClienteErpActualizadoData(clienteId, "ERP-COB", corte.AddDays(-1100), 18, 14500m, "GTQ", 18, 0, 0, 1, 0, 0, corte, "LOTE-COB"),
                CancellationToken.None);
        }

        using (var alcance = proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioEvaluacionCredito>().EvaluarAsync(
                new EvaluacionCreditoSolicitadaData(ordenId, ordenId[^6..], clienteId, Canal.WEB, Moneda.GTQ, monto, DateTime.UtcNow),
                CancellationToken.None);
        }

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuenta = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.OrdenId == ordenId);
        return (clienteId, ordenId, cuenta.Id);
    }

    private static async Task<AbonoDto> AbonarAsync(ServiceProvider proveedor, string clienteId, Guid cuentaId, decimal monto)
    {
        using var alcance = proveedor.CreateScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<ServicioAbonos>()
            .CrearAsync(clienteId, cuentaId.ToString("N"), monto, "tok_integracion", CancellationToken.None);
        Assert.True(resultado.Exito, resultado.MensajeError);
        return resultado.Abono!;
    }

    private static async Task<ResultadoCobroAbono> CobradoAsync(ServiceProvider proveedor, AbonoDto abono, string clienteId)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<ServicioResultadoAbonos>().CobradoAsync(
            new AbonoCreditoCobradoData(abono.Id, abono.Referencia, clienteId, "GTQ", abono.Monto, "PK-INT", "4242", "VISA", DateTime.UtcNow),
            CancellationToken.None);
    }

    [Fact]
    public async Task Un_abono_aplicado_persiste_los_saldos_de_la_cuenta_y_la_linea()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var (clienteId, _, cuentaId) = await CompraAsync(proveedor, 1000m);

        var abono = await AbonarAsync(proveedor, clienteId, cuentaId, 400m);
        Assert.Matches(@"^ABO-\d{4}-\d{6}$", abono.Referencia);
        await CobradoAsync(proveedor, abono, clienteId);

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuenta = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.Id == cuentaId);
        Assert.Equal(600m, cuenta.SaldoPendiente);
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, cuenta.Estado);
        Assert.Equal(600m, (await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).SaldoUtilizado);
        Assert.Equal(EstadoAbonoCredito.APLICADO, (await contexto.Abonos.AsNoTracking().SingleAsync(a => a.CuentaId == cuentaId)).Estado);
        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.EventType == AbonoCreditoAplicado.EventType && m.AggregateId == abono.Id));
        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.EventType == CobroAbonoCreditoSolicitado.EventType && m.AggregateId == abono.Id));

        // Pagar el resto deja la cuenta PAGADA y puntual.
        var resto = await AbonarAsync(proveedor, clienteId, cuentaId, 600m);
        await CobradoAsync(proveedor, resto, clienteId);
        await using var verificacion = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var pagada = await verificacion.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.Id == cuentaId);
        Assert.Equal(EstadoCuentaPorCobrar.PAGADA, pagada.Estado);
        Assert.True(pagada.PagoPuntual);
        Assert.NotNull(pagada.PagadaEn);
        Assert.Equal(0m, (await verificacion.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).SaldoUtilizado);
        Assert.Equal(1, await verificacion.OutboxMessages.CountAsync(m => m.EventType == CuentaCreditoPagada.EventType && m.AggregateId == cuentaId.ToString("N")));
    }

    [Fact]
    public async Task El_cobro_repetido_no_aplica_dos_veces()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var (clienteId, _, cuentaId) = await CompraAsync(proveedor, 1000m);
        var abono = await AbonarAsync(proveedor, clienteId, cuentaId, 300m);

        var primero = await CobradoAsync(proveedor, abono, clienteId);
        var segundo = await CobradoAsync(proveedor, abono, clienteId);

        Assert.Equal(ResultadoCobroAbono.Aplicado, primero);
        Assert.Equal(ResultadoCobroAbono.Ignorado, segundo);
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        Assert.Equal(700m, (await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.Id == cuentaId)).SaldoPendiente);
        Assert.Equal(700m, (await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).SaldoUtilizado);
        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.EventType == AbonoCreditoAplicado.EventType && m.AggregateId == abono.Id));
    }

    [Fact]
    public async Task El_indice_parcial_impide_dos_abonos_en_curso_para_la_misma_cuenta()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var (clienteId, _, cuentaId) = await CompraAsync(proveedor, 1000m);
        await AbonarAsync(proveedor, clienteId, cuentaId, 100m);

        // Se salta la verificacion del servicio para probar la base: el indice choca.
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuenta = await contexto.CuentasPorCobrar.SingleAsync(c => c.Id == cuentaId);
        contexto.Abonos.Add(new AbonoCredito($"ABO-2026-9{Random.Shared.Next(10000, 99999)}", cuenta, 50m, DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
    }

    [Fact]
    public async Task El_vencimiento_persiste_la_cuenta_VENCIDA_y_la_linea_en_pausa()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var (clienteId, ordenId, cuentaId) = await CompraAsync(proveedor, 800m);

        using (var alcance = proveedor.CreateScope())
        {
            Assert.True(await alcance.ServiceProvider.GetRequiredService<ServicioDemoCredito>()
                .FijarVencimientoAsync(ordenId, -1, CancellationToken.None));
        }

        using (var alcance = proveedor.CreateScope())
        {
            var resultado = await alcance.ServiceProvider.GetRequiredService<ServicioVencimientos>().ProcesarAsync(CancellationToken.None);
            Assert.True(resultado.Vencidas >= 1);
        }

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        Assert.Equal(EstadoCuentaPorCobrar.VENCIDA, (await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.Id == cuentaId)).Estado);
        Assert.Equal(EstadoLineaCredito.BLOQUEADA, (await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).Estado);
        var perfil = await contexto.Perfiles.AsNoTracking().SingleAsync(p => p.ClienteId == clienteId);
        Assert.Equal(CodigosMotivoCredito.DeudaVencida, perfil.CodigoMotivo);
        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.EventType == CuentaCreditoVencida.EventType && m.AggregateId == cuentaId.ToString("N")));

        // Una segunda pasada no vuelve a vencerla.
        using (var alcance = proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioVencimientos>().ProcesarAsync(CancellationToken.None);
        }

        Assert.Equal(1, await contexto.OutboxMessages.CountAsync(m => m.EventType == CuentaCreditoVencida.EventType && m.AggregateId == cuentaId.ToString("N")));
    }
}
