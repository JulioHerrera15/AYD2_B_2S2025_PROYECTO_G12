using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SgoLogic.BuildingBlocks.HealthChecks;

/// <summary>
/// Verifica que se puede abrir una conexion real a PostgreSQL y ejecutar una
/// consulta minima. No depende de EF Core ni del DbContext de ningun servicio.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly string _cadenaConexion;

    public PostgresHealthCheck(string cadenaConexion)
    {
        _cadenaConexion = cadenaConexion;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conexion = new NpgsqlConnection(_cadenaConexion);
            await conexion.OpenAsync(cancellationToken);

            await using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT 1";
            await comando.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("No fue posible conectar a PostgreSQL", ex);
        }
    }
}