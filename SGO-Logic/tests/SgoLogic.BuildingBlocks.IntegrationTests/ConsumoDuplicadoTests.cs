using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

[Collection("integracion")]
public sealed class ConsumoDuplicadoTests
{
    private readonly IntegracionFixture _fixture;

    public ConsumoDuplicadoTests(IntegracionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Consumo_duplicado_del_mismo_eventId_es_idempotente()
    {
        var configuracion = IntegracionServicios.CrearConfiguracion(_fixture.RabbitMq.GetConnectionString(), "test-duplicado");
        await using var proveedor = IntegracionServicios.ConstruirProveedor(_fixture.Postgres.GetConnectionString(), configuracion);

        var conexion = new ConexionRabbitMq(configuracion);
        var suscriptor = new SuscriptorRabbitMq(conexion, NullLogger<SuscriptorRabbitMq>.Instance);
        var contador = new ContadorLlamadas();
        var cola = $"test.duplicado.{Guid.NewGuid():N}";
        var routingKey = $"test.duplicado.{Guid.NewGuid():N}";

        var consumidor = new TestConsumidor(
            proveedor.GetRequiredService<IServiceScopeFactory>(),
            conexion,
            suscriptor,
            IntegracionServicios.CrearLogger<TestConsumidor>(),
            contador,
            cola,
            [new BindingCola(TopologiaMensajeria.ExchangeEventos, routingKey)]);

        await consumidor.StartAsync(CancellationToken.None);
        try
        {
            var evento = new IntegrationEvent<DatoPrueba>
            {
                EventType = "DatoPrueba.Creado",
                Sequence = 1,
                TransactionId = Guid.NewGuid().ToString(),
                AggregateType = "DatoPrueba",
                AggregateId = Guid.NewGuid().ToString(),
                Source = "test",
                Data = new DatoPrueba("x")
            };

            var cuerpo = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evento, SgoJsonSerializerOptions.Default));
            var publicador = new PublicadorRabbitMq(conexion);

            // Primera entrega: se procesa.
            await publicador.PublicarAsync(TopologiaMensajeria.ExchangeEventos, routingKey, cuerpo);
            await EsperarHastaAsync(() => contador.Valor >= 1, TimeSpan.FromSeconds(10));
            Assert.Equal(1, contador.Valor);

            // Redelivery del mismo eventId (el broker entrega al menos una vez):
            // debe hacer ack y descartar sin volver a llamar ManejarAsync.
            await publicador.PublicarAsync(TopologiaMensajeria.ExchangeEventos, routingKey, cuerpo);
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(1, contador.Valor);

            await using var verificacion = IntegracionServicios.CrearDbContext(_fixture.Postgres.GetConnectionString());
            var vecesRegistrado = await verificacion.MensajesProcesados
                .CountAsync(m => m.EventId == evento.EventId && m.NombreServicio == "test-consumidor");

            Assert.Equal(1, vecesRegistrado);
        }
        finally
        {
            await consumidor.StopAsync(CancellationToken.None);
            conexion.Dispose();
        }
    }

    private static async Task EsperarHastaAsync(Func<bool> condicion, TimeSpan tiempoMaximo)
    {
        var limite = DateTime.UtcNow.Add(tiempoMaximo);
        while (!condicion() && DateTime.UtcNow < limite)
        {
            await Task.Delay(100);
        }
    }
}