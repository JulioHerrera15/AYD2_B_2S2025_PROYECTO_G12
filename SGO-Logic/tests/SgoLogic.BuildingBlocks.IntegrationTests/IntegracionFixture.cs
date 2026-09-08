using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

/// <summary>
/// Un Postgres y un RabbitMQ reales (Testcontainers) compartidos por toda la
/// coleccion "integracion". Los tests de esa coleccion corren secuenciales (no en
/// paralelo entre si, xUnit lo garantiza dentro de una misma coleccion), asi que
/// es seguro que alguno pare/arranque el contenedor de RabbitMQ sin afectar a los
/// demas.
/// </summary>
public sealed class IntegracionFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sgo_integracion")
        .WithUsername("sgo_test")
        .WithPassword("sgo_test")
        .Build();

    // Puerto de host fijo (no el aleatorio por defecto de Testcontainers): en este
    // Docker, un stop/start del mismo contenedor puede reasignar el puerto host
    // aleatorio, lo que invalidaria la cadena de conexion que BrokerCaidoTests
    // captura antes de parar el broker. Con puerto fijo, sobrevive al restart.
    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .WithPortBinding(25673, 5672)
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());

        await using var contexto = IntegracionServicios.CrearDbContext(Postgres.GetConnectionString());
        await contexto.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }
}

[CollectionDefinition("integracion")]
public sealed class IntegracionCollection : ICollectionFixture<IntegracionFixture>;