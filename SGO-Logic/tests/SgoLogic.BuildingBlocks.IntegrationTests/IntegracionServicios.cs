using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;

namespace SgoLogic.BuildingBlocks.IntegrationTests;

/// <summary>
/// Arma a mano las piezas que en un servicio real arma AddSgoMessaging /
/// AddOutbox, para poder controlar el ciclo de vida de cada una (StartAsync /
/// StopAsync) directamente desde el test en vez de a traves de un Host completo.
/// </summary>
public static class IntegracionServicios
{
    public static TestDbContext CrearDbContext(string cadenaConexionPostgres)
    {
        var opciones = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(cadenaConexionPostgres)
            .Options;

        return new TestDbContext(opciones);
    }

    public static IConfiguration CrearConfiguracion(
        string cadenaConexionRabbitMq,
        string source,
        int pollingIntervalMs = 300,
        int maxReintentos = 3,
        int batchSize = 20)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = cadenaConexionRabbitMq,
                ["Outbox:Source"] = source,
                ["Outbox:PollingIntervalMs"] = pollingIntervalMs.ToString(),
                ["Outbox:MaxReintentos"] = maxReintentos.ToString(),
                ["Outbox:BatchSize"] = batchSize.ToString()
            })
            .Build();
    }

    /// <summary>
    /// Contenedor minimo: DbContext contra Postgres + IOutboxWriter (via el mismo
    /// AddOutbox publico que usa un servicio real). AddOutbox tambien registra
    /// OutboxPublisher&lt;TestDbContext&gt; como IHostedService, pero eso es
    /// inofensivo aca: un ServiceProvider suelto (sin IHost) nunca lo arranca solo,
    /// asi que cada test sigue controlando a mano cuando arranca su propio
    /// publicador.
    /// </summary>
    public static ServiceProvider ConstruirProveedor(string cadenaConexionPostgres, IConfiguration configuracion)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseNpgsql(cadenaConexionPostgres));
        services.AddSingleton(configuracion);
        services.AddOutbox<TestDbContext>(configuracion);

        return services.BuildServiceProvider();
    }

    public static ILogger<T> CrearLogger<T>() =>
        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<T>();

    /// <summary>
    /// Las mismas opciones que CrearConfiguracion coloca bajo la seccion "Outbox",
    /// pero como objeto directo: evita depender de ConfigurationBinder.Get&lt;T&gt;()
    /// solo para construir un IOptions&lt;OutboxOptions&gt; a mano en un test.
    /// </summary>
    public static OutboxOptions CrearOpcionesOutbox(
        string source,
        int pollingIntervalMs = 300,
        int maxReintentos = 3,
        int batchSize = 20) => new()
    {
        Source = source,
        PollingIntervalMs = pollingIntervalMs,
        MaxReintentos = maxReintentos,
        BatchSize = batchSize
    };
}