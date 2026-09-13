using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

/// <summary>
/// Fabrica de diseno para las herramientas de EF Core (dotnet ef). Evita que
/// dotnet ef ejecute Program.cs (y su Database.Migrate() de arranque) para
/// construir el modelo. La cadena de conexion sale de ConnectionStrings__Postgres
/// y, si no esta, de appsettings.Development.json.
/// </summary>
public sealed class FacturacionDbContextFactory : IDesignTimeDbContextFactory<FacturacionDbContext>
{
    public FacturacionDbContext CreateDbContext(string[] args)
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

        var opciones = new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseNpgsql(cadenaConexion)
            .Options;

        return new FacturacionDbContext(opciones);
    }
}
