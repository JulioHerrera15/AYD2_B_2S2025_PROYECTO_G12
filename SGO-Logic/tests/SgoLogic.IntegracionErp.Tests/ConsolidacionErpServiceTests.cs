using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.IntegracionErp.Worker.Application;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Tests;

public sealed class ConsolidacionErpServiceTests
{
    [Fact]
    public async Task Marca_lote_como_FALLIDO_al_agotar_intentos_persistentes()
    {
        var options = new DbContextOptionsBuilder<IntegracionErpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var contexto = new IntegracionErpDbContext(options);
        contexto.Ventas.Add(new VentaErp
        {
            EventoId = "evt-1", OrdenId = "ord-1", NumeroOrden = "ORD-1", Canal = "WEB",
            Tienda = "T01", Monto = 100m, FechaVenta = DateTime.UtcNow
        });
        await contexto.SaveChangesAsync();

        var servicio = new ConsolidacionErpService(
            contexto,
            new GeneradorLoteErp(),
            new ClienteFallido(),
            new OutboxNulo(),
            Options.Create(new ErpOptions { MaxIntentosLote = 1, MaxReintentos = 0 }),
            NullLogger<ConsolidacionErpService>.Instance);

        var lote = await servicio.ConsolidarYTransferirAsync(CancellationToken.None);

        Assert.NotNull(lote);
        Assert.Equal("FALLIDO", lote.Estado);
        Assert.Equal(1, lote.Intentos);
    }

    [Fact]
    public async Task Conciliacion_reporta_faltantes_extras_y_es_idempotente_por_dia()
    {
        var options = new DbContextOptionsBuilder<IntegracionErpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var contexto = new IntegracionErpDbContext(options);
        contexto.Lotes.AddRange(
            new LoteErp
            {
                LoteId = "LOTE-OK", Secuencia = 1, Estado = "ACEPTADO", EnviadoEn = DateTime.UtcNow,
                CantidadRegistros = 2, MontoTotal = 100m
            },
            new LoteErp
            {
                LoteId = "LOTE-FALTANTE", Secuencia = 2, Estado = "ACEPTADO", EnviadoEn = DateTime.UtcNow,
                CantidadRegistros = 1, MontoTotal = 30m
            });
        await contexto.SaveChangesAsync();

        var cliente = new ClienteConResumen(new ResumenErp(
            3,
            [
                new ConfirmacionErp("LOTE-OK", 1, "ACEPTADO", 2, 100m),
                new ConfirmacionErp("LOTE-EXTRA", 3, "ACEPTADO", 3, 70m)
            ]));
        var servicio = new ConsolidacionErpService(
            contexto,
            new GeneradorLoteErp(),
            cliente,
            new OutboxNulo(),
            Options.Create(new ErpOptions()),
            NullLogger<ConsolidacionErpService>.Instance);

        await servicio.ConciliarAsync(CancellationToken.None);
        await servicio.ConciliarAsync(CancellationToken.None);

        var reportes = contexto.Conciliaciones.ToList();
        Assert.Equal(3, reportes.Count);
        Assert.True(reportes.Single(x => x.LoteId == "LOTE-OK").Conciliada);

        var faltante = reportes.Single(x => x.LoteId == "LOTE-FALTANTE");
        Assert.False(faltante.Conciliada);
        Assert.Equal(0, faltante.RegistrosErp);
        Assert.Contains("No existe confirmacion", faltante.Diferencias);

        var extra = reportes.Single(x => x.LoteId == "LOTE-EXTRA");
        Assert.False(extra.Conciliada);
        Assert.Equal(3, extra.RegistrosErp);
        Assert.Contains("sin registro local", extra.Diferencias);
    }

    private sealed class ClienteFallido : IClienteErp
    {
        public Task<RespuestaErp> EnviarLoteAsync(string contenido, string contentType, CancellationToken cancelacion = default)
            => Task.FromResult(new RespuestaErp(false, false, "NO_DISPONIBLE", 503, "caido", "caido"));

        public Task<ResumenErp?> ObtenerResumenAsync(CancellationToken cancelacion = default)
            => Task.FromResult<ResumenErp?>(null);

        public Task<IReadOnlyList<ClienteHistorialErp>> ObtenerHistorialClientesAsync(DateTime? modificadosDesde = null, CancellationToken cancelacion = default)
            => Task.FromResult<IReadOnlyList<ClienteHistorialErp>>([]);
    }

    private sealed class ClienteConResumen : IClienteErp
    {
        private readonly ResumenErp _resumen;

        public ClienteConResumen(ResumenErp resumen)
        {
            _resumen = resumen;
        }

        public Task<RespuestaErp> EnviarLoteAsync(string contenido, string contentType, CancellationToken cancelacion = default)
            => throw new NotSupportedException();

        public Task<ResumenErp?> ObtenerResumenAsync(CancellationToken cancelacion = default)
            => Task.FromResult<ResumenErp?>(_resumen);

        public Task<IReadOnlyList<ClienteHistorialErp>> ObtenerHistorialClientesAsync(DateTime? modificadosDesde = null, CancellationToken cancelacion = default)
            => Task.FromResult<IReadOnlyList<ClienteHistorialErp>>([]);
    }

    private sealed class OutboxNulo : IOutboxWriter
    {
        public void Encolar<TData>(string eventType, string aggregateType, string aggregateId, int sequence, string exchange, string routingKey, TData data)
            where TData : notnull
        {
        }
    }
}