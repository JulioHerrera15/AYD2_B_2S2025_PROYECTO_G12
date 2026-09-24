using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SgoLogic.IntegracionErp.Worker.Infrastructure;

public sealed class IntegracionErpDbContextFactory : IDesignTimeDbContextFactory<IntegracionErpDbContext>
{
    public IntegracionErpDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IntegracionErpDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=sgo_integracion_erp;Username=sgo_admin;Password=change_me")
            .Options;
        return new IntegracionErpDbContext(options);
    }
}
