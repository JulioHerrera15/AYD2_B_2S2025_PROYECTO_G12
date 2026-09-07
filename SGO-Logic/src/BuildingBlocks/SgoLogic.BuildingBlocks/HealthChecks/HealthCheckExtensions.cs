using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SgoLogic.BuildingBlocks.HealthChecks;

/// <summary>
/// Registra y expone los dos health checks estandar de todo servicio SGO-Logic:
/// /health/live (proceso vivo, sin dependencias) y /health/ready (conexion real a
/// PostgreSQL, tomada de ConnectionStrings:Postgres).
/// </summary>
public static class HealthCheckExtensions
{
    private const string EtiquetaReady = "ready";

    public static IServiceCollection AddSgoHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var cadenaConexion = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Falta la cadena de conexion 'Postgres' en la configuracion.");

        services.AddHealthChecks()
            .AddCheck(EtiquetaReady, new PostgresHealthCheck(cadenaConexion), tags: new[] { EtiquetaReady });

        return services;
    }

    public static IEndpointRouteBuilder MapSgoHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registro => registro.Tags.Contains(EtiquetaReady)
        });

        return endpoints;
    }
}