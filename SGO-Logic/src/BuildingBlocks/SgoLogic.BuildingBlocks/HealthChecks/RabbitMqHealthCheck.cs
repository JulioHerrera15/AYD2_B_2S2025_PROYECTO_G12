using Microsoft.Extensions.Diagnostics.HealthChecks;
using SgoLogic.BuildingBlocks.Messaging;

namespace SgoLogic.BuildingBlocks.HealthChecks;

/// <summary>Verifica que la conexion compartida a RabbitMQ este abierta.</summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IConexionRabbitMq _conexion;

    public RabbitMqHealthCheck(IConexionRabbitMq conexion)
    {
        _conexion = conexion;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var abierta = _conexion.ObtenerConexion().IsOpen;

            return Task.FromResult(abierta
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("La conexion a RabbitMQ no esta abierta."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("No fue posible conectar a RabbitMQ.", ex));
        }
    }
}