using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Auditoria.Worker.Application;
using SgoLogic.Auditoria.Worker.Consumers;
using SgoLogic.Auditoria.Worker.Infrastructure;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Auditoria.Worker.Domain;

namespace SgoLogic.Auditoria.Tests;

[Collection("auditoria")]
public sealed class PersistenciaE2eTests
{
    private readonly AuditoriaFixture _fixture;

    public PersistenciaE2eTests(AuditoriaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Publicar_evento_por_RabbitMQ_persiste_la_evidencia_en_PostgreSQL()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = _fixture.RabbitMq.GetConnectionString()
            })
            .Build();
        using var conexion = new ConexionRabbitMq(configuracion);
        using var proveedor = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AuditoriaDbContext>(opciones => opciones.UseNpgsql(_fixture.CadenaConexionAppRestringida()))
            .AddSingleton<IConexionRabbitMq>(conexion)
            .AddSingleton<ISuscriptorMensajes>(servicios => new SuscriptorRabbitMq(
                servicios.GetRequiredService<IConexionRabbitMq>(),
                NullLogger<SuscriptorRabbitMq>.Instance))
            .AddSingleton<RegistradorEventoAuditoria>()
            .BuildServiceProvider();

        var consumidor = new AuditoriaConsumidor(
            proveedor.GetRequiredService<IServiceScopeFactory>(),
            conexion,
            proveedor.GetRequiredService<ISuscriptorMensajes>(),
            proveedor.GetRequiredService<RegistradorEventoAuditoria>(),
            NullLogger<AuditoriaConsumidor>.Instance);

        await consumidor.StartAsync(CancellationToken.None);

        try
        {
            var evento = EventosDePrueba.Crear(
                transactionId: $"tx-e2e-{Guid.NewGuid():N}",
                aggregateId: "LOTE-E2E-001",
                eventType: "LoteErpProcesado",
                source: "integracion-erp",
                data: new { loteId = "LOTE-E2E-001", estado = "ACEPTADO", montoTotal = 125.75m });
            var cuerpo = JsonSerializer.SerializeToUtf8Bytes(evento, SgoJsonSerializerOptions.Default);
            var publicador = new PublicadorRabbitMq(conexion);

            await publicador.PublicarAsync(
                TopologiaMensajeria.ExchangeEventos,
                "integracion-erp.lote.procesado",
                cuerpo,
                CancellationToken.None);

            EventoAuditoria? persistido = null;
            var limite = DateTime.UtcNow.AddSeconds(10);
            while (persistido is null && DateTime.UtcNow < limite)
            {
                await using var lectura = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
                persistido = await lectura.EventosAuditoria
                    .SingleOrDefaultAsync(x => x.EventId == evento.EventId);
                if (persistido is null)
                    await Task.Delay(100);
            }

            Assert.NotNull(persistido);
            Assert.Equal(evento.EventId, persistido.EventId);
            Assert.Equal("LoteErpProcesado", persistido.EventType);
            Assert.Equal("integracion-erp", persistido.Source);
            Assert.Equal("LOTE-E2E-001", persistido.AggregateId);
            Assert.Contains("LOTE-E2E-001", persistido.Payload);
        }
        finally
        {
            await consumidor.StopAsync(CancellationToken.None);
        }
    }
}