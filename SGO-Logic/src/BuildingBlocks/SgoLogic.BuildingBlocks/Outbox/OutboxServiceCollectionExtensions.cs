using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SgoLogic.BuildingBlocks.Outbox;

/// <summary>
/// Registra el escritor de outbox y el publicador de fondo para TDbContext.
/// Requiere haber llamado AddSgoMessaging() antes (mismo Program.cs, cualquier
/// orden entre ambos, pero AddSgoMessaging primero para que la topologia se
/// declare al arrancar antes que el primer ciclo de polling).
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TDbContext>(this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Seccion));
        services.AddScoped<IOutboxWriter, OutboxWriter<TDbContext>>();
        services.AddHostedService<OutboxPublisher<TDbContext>>();

        return services;
    }
}