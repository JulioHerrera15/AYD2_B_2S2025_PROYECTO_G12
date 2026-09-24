using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SgoLogic.Auth.Api.Infrastructure;

/// <summary>
/// Factory de diseno para que dotnet-ef construya el modelo sin ejecutar Program.cs.
/// Lee ConnectionStrings__Postgres de entorno; si no esta, usa appsettings.Development.json.
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=sgo_auth;Username=sgo_admin;Password=change_me";

        var opciones = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AuthDbContext(opciones);
    }
}
