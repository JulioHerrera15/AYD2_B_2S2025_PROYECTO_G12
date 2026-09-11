using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SgoLogic.Notificaciones.Api.Infrastructure;

public sealed class NotificacionesDbContextFactory : IDesignTimeDbContextFactory<NotificacionesDbContext>
{
    public NotificacionesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificacionesDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=sgo_notificaciones;Username=sgo_admin;Password=change_me");
        return new NotificacionesDbContext(optionsBuilder.Options);
    }
}
