using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia;

/// <summary>Solo para `dotnet ef` en tiempo de diseno; no se usa en ejecucion.</summary>
public sealed class DespachoDbContextFactory : IDesignTimeDbContextFactory<DespachoDbContext>
{
    public DespachoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DespachoDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=sgo_despacho;Username=postgres;Password=postgres")
            .Options);
}
