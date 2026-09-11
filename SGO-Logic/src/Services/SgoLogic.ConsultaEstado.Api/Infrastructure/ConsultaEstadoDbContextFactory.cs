using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SgoLogic.ConsultaEstado.Api.Infrastructure;

public sealed class ConsultaEstadoDbContextFactory : IDesignTimeDbContextFactory<ConsultaEstadoDbContext>
{
    public ConsultaEstadoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=sgo_consulta_estado;Username=sgo_admin;Password=change_me";

        var opciones = new DbContextOptionsBuilder<ConsultaEstadoDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ConsultaEstadoDbContext(opciones);
    }
}
