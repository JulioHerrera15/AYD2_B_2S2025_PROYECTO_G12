using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// Health check de la base propia del contexto: comprueba que el DbContext puede
/// abrir conexion contra sgo_ordenes. Se expone bajo /health/ready.
/// </summary>
public sealed class OrdenesDbHealthCheck : IHealthCheck
{
    private readonly OrdenesDbContext _contexto;

    public OrdenesDbHealthCheck(OrdenesDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var puedeConectar = await _contexto.Database.CanConnectAsync(cancellationToken);
            return puedeConectar
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("No fue posible conectar a la base sgo_ordenes.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Fallo la verificacion de la base sgo_ordenes.", ex);
        }
    }
}
