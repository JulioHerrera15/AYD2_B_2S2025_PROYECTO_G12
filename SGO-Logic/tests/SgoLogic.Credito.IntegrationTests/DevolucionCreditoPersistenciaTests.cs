using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Devolucion de una compra a credito contra PostgreSQL real: movimiento unico por
/// devolucion, saldo de la cuenta y de la linea, y la solicitud de reembolso del abono
/// en el outbox de la misma transaccion.
/// </summary>
[Collection("credito-integracion")]
public sealed class DevolucionCreditoPersistenciaTests
{
    private readonly CreditoFixture _fixture;

    public DevolucionCreditoPersistenciaTests(CreditoFixture fixture)
    {
        _fixture = fixture;
    }

    private static string OrdenNueva() => $"ORD-2026-{Random.Shared.Next(100000, 999999)}{Guid.NewGuid():N}"[..20];

    private async Task<(string ClienteId, string OrdenId, Guid CuentaId)> CompraAsync(ServiceProvider proveedor, decimal monto)
    {
        var clienteId = $"cliente-{Guid.NewGuid():N}@sgo.local";
        var ordenId = OrdenNueva();
        var corte = DateTime.UtcNow;

        using (var alcance = proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>().AplicarHistorialAsync(
                new HistorialClienteErpActualizadoData(clienteId, "ERP-DEV", corte.AddDays(-1100), 18, 14500m, "GTQ", 18, 0, 0, 1, 0, 0, corte, "LOTE-DEV"),
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

    private static DevolucionRegistradaData Devolucion(string devolucionId, string ordenId, string clienteId, decimal monto, bool esParcial) =>
        new(devolucionId, ordenId, clienteId, TipoDevolucion.DEVOLUCION, esParcial, "UBI-01",
            new[] { new DevolucionLineaData("SKU-1", 1, "No le gusto", null) },
            "GTQ", monto.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), "CRE-1", "", clienteId,
            DateTime.UtcNow, MedioPago.CREDITO);

    private static async Task<ResultadoDevolucionCredito> DevolverAsync(ServiceProvider proveedor, DevolucionRegistradaData datos)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<ServicioDevolucionesCredito>().ProcesarAsync(datos, CancellationToken.None);
    }

    [Fact]
    public async Task Una_devolucion_parcial_persiste_movimiento_saldo_y_linea_una_sola_vez()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var (clienteId, ordenId, cuentaId) = await CompraAsync(proveedor, 1000m);
        var devolucionId = $"DEV-{Guid.NewGuid():N}"[..20];
        var datos = Devolucion(devolucionId, ordenId, clienteId, 350m, esParcial: true);

        Assert.Equal(ResultadoDevolucionCredito.Aplicada, await DevolverAsync(proveedor, datos));
        Assert.Equal(ResultadoDevolucionCredito.YaAplicada, await DevolverAsync(proveedor, datos));

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuenta = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.Id == cuentaId);
        Assert.Equal(650m, cuenta.SaldoPendiente);
        Assert.Equal(EstadoCuentaPorCobrar.VIGENTE, cuenta.Estado);
        Assert.Equal(650m, (await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).SaldoUtilizado);

        var movimiento = await contexto.Movimientos.AsNoTracking().SingleAsync(m => m.CuentaId == cuentaId);
        Assert.Equal(TipoMovimientoCuenta.DEVOLUCION, movimiento.Tipo);
        Assert.Equal(devolucionId, movimiento.Referencia);
        Assert.Equal(350m, movimiento.Monto);
        Assert.Equal(350m, movimiento.MontoDescontadoDeuda);

        var eventos = await contexto.OutboxMessages.AsNoTracking()
            .Where(m => m.AggregateId == cuentaId.ToString("N") && m.EventType == DevolucionCreditoAplicada.EventType)
            .CountAsync();
        Assert.Equal(1, eventos);
    }

    [Fact]
    public async Task Una_devolucion_total_de_una_compra_pagada_pide_el_reembolso_del_abono_en_la_misma_transaccion()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var (clienteId, ordenId, cuentaId) = await CompraAsync(proveedor, 500m);

        AbonoDto abono;
        using (var alcance = proveedor.CreateScope())
        {
            var resultado = await alcance.ServiceProvider.GetRequiredService<ServicioAbonos>()
                .CrearAsync(clienteId, cuentaId.ToString("N"), 500m, "tok_integracion", CancellationToken.None);
            abono = resultado.Abono!;
        }

        using (var alcance = proveedor.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ServicioResultadoAbonos>().CobradoAsync(
                new AbonoCreditoCobradoData(abono.Id, abono.Referencia, clienteId, "GTQ", 500m, "PK-INT", "4242", "VISA", DateTime.UtcNow),
                CancellationToken.None);
        }

        await DevolverAsync(proveedor, Devolucion($"DEV-{Guid.NewGuid():N}"[..20], ordenId, clienteId, 500m, esParcial: false));

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var guardado = await contexto.Abonos.AsNoTracking().SingleAsync(a => a.CuentaId == cuentaId);
        Assert.Equal(500m, guardado.MontoReembolsoSolicitado);
        Assert.NotNull(guardado.ReembolsoSolicitadoEn);
        Assert.Equal(1, await contexto.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.AggregateId == abono.Id && m.EventType == ReembolsoAbonoCreditoSolicitado.EventType));
        Assert.Equal(0m, (await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId)).SaldoUtilizado);
    }
}
