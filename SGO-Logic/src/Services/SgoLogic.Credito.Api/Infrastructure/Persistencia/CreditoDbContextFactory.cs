using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia;

/// <summary>
/// Fabrica de diseno para dotnet ef: evita ejecutar Program.cs (y su
/// Database.Migrate() y siembra de arranque) para construir el modelo. La cadena
/// sale de ConnectionStrings__Postgres y, si no esta, de appsettings.Development.json.
/// </summary>
public sealed class CreditoDbContextFactory : IDesignTimeDbContextFactory<CreditoDbContext>
{
    public CreditoDbContext CreateDbContext(string[] args)
    {
        var cadenaConexion = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        if (string.IsNullOrWhiteSpace(cadenaConexion))
        {
            var configuracion = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            cadenaConexion = configuracion.GetConnectionString("Postgres");
        }

        if (string.IsNullOrWhiteSpace(cadenaConexion))
        {
            throw new InvalidOperationException(
                "No se encontro la cadena de conexion. Defina ConnectionStrings__Postgres " +
                "o ConnectionStrings:Postgres en appsettings.Development.json.");
        }

        var opciones = new DbContextOptionsBuilder<CreditoDbContext>()
            .UseNpgsql(cadenaConexion)
            .Options;

        return new CreditoDbContext(opciones);
    }
}
