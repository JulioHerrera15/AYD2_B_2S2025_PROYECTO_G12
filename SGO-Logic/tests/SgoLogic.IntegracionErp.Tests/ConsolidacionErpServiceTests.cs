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

    private sealed class ClienteFallido : IClienteErp
    {
        public Task<RespuestaErp> EnviarLoteAsync(string contenido, string contentType, CancellationToken cancelacion = default)
            => Task.FromResult(new RespuestaErp(false, false, "NO_DISPONIBLE", 503, "caido", "caido"));

        public Task<ResumenErp?> ObtenerResumenAsync(CancellationToken cancelacion = default)
            => Task.FromResult<ResumenErp?>(null);
    }

    private sealed class OutboxNulo : IOutboxWriter
    {
        public void Encolar<TData>(string eventType, string aggregateType, string aggregateId, int sequence, string exchange, string routingKey, TData data)
            where TData : notnull
        {
        }
    }
}