using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Arma el contenedor real de Credito contra Postgres: los mismos repositorios,
/// outbox y servicios que registra Program.cs. Nada esta sustituido por un doble.
///
/// No hace falta RabbitMQ: IOutboxWriter solo escribe filas en outbox_messages en
/// la misma transaccion, y el OutboxPublisher (hosted service) aqui nunca arranca.
/// </summary>
public static class CreditoServicios
{
    public static CreditoDbContext CrearDbContext(string cadenaConexion) =>
        new(new DbContextOptionsBuilder<CreditoDbContext>()
            .UseNpgsql(cadenaConexion)
            .Options);

    public static ServiceProvider CrearProveedor(string cadenaConexion)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:Source"] = "credito",
                ["Outbox:PollingIntervalMs"] = "1000",
                ["Outbox:MaxReintentos"] = "3",
                ["Outbox:BatchSize"] = "20"
            })
            .Build();

        var servicios = new ServiceCollection();

        servicios.AddLogging();
        servicios.AddDbContext<CreditoDbContext>(opciones => opciones.UseNpgsql(cadenaConexion));
        servicios.AddOutbox<CreditoDbContext>(configuracion);
        servicios.Configure<PoliticaCreditoOptions>(configuracion.GetSection(PoliticaCreditoOptions.Seccion));
        servicios.AddSingleton(proveedor => new PoliticaCredito(proveedor.GetRequiredService<IOptions<PoliticaCreditoOptions>>()));

        servicios.AddScoped<IRepositorioCredito, RepositorioCredito>();
        servicios.AddScoped<IConsultasCredito, ConsultasCredito>();
        servicios.AddScoped<ServicioPerfilCrediticio>();
        servicios.AddScoped<ServicioElegibilidad>();
        servicios.AddScoped<ServicioEvaluacionCredito>();
        servicios.AddScoped<ServicioAnulacionCargos>();
        servicios.AddScoped<ServicioDemoCredito>();

        return servicios.BuildServiceProvider();
    }
}
