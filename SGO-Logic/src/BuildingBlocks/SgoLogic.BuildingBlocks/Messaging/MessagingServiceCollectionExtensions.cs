using Microsoft.Extensions.DependencyInjection;
using SgoLogic.BuildingBlocks.HealthChecks;

namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Registra la mensajeria sobre RabbitMQ.Client: conexion compartida, declarador
/// de topologia, publicador, suscriptor y el check de /health/ready que valida el
/// broker (ademas del de PostgreSQL, ya registrado por AddSgoDefaults). Llamar
/// antes de AddOutbox y antes de registrar cualquier EventConsumerBase, para que
/// la topologia se declare primero.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddSgoMessaging(this IServiceCollection services)
    {
        services.AddSingleton<IConexionRabbitMq, ConexionRabbitMq>();
        services.AddSingleton<DeclaradorTopologia>();
        services.AddSingleton<IPublicadorMensajes, PublicadorRabbitMq>();
        services.AddSingleton<ISuscriptorMensajes, SuscriptorRabbitMq>();
        services.AddHostedService<TopologiaInicialHostedService>();

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("ready-rabbitmq", tags: new[] { HealthCheckExtensions.EtiquetaReady });

        return services;
    }
}