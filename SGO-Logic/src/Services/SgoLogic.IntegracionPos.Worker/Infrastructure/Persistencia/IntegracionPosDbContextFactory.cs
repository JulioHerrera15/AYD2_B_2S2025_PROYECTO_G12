using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SgoLogic.IntegracionPos.Worker.Infrastructure.Persistencia;

public sealed class IntegracionPosDbContextFactory : IDesignTimeDbContextFactory<IntegracionPosDbContext>
{
    public IntegracionPosDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IntegracionPosDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=sgo_integracion_pos;Username=sgo_admin;Password=change_me")
            .Options;
        return new IntegracionPosDbContext(options);
    }
}