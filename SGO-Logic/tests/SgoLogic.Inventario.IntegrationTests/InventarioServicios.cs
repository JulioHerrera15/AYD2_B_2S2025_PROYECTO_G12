using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;

namespace SgoLogic.Inventario.IntegrationTests;

/// <summary>
/// Arma el contenedor real de Inventario contra Postgres: los mismos repositorios,
/// unidad de trabajo y servicios de aplicacion que registra <c>Program.cs</c>.
/// Nada esta sustituido por un doble — ese es el punto de esta suite.
///
/// No hace falta RabbitMQ: <see cref="IOutboxWriter" /> solo escribe filas en la
/// tabla outbox dentro de la misma transaccion, y quien las publica
/// (OutboxPublisher) es un hosted service que aqui nunca arranca.
/// </summary>
public static class InventarioServicios
{
    public static InventarioDbContext CrearDbContext(string cadenaConexion) =>
        new(new DbContextOptionsBuilder<InventarioDbContext>()
            .UseNpgsql(cadenaConexion)
            .Options);

    public static ServiceProvider CrearProveedor(string cadenaConexion)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:Source"] = "inventario",
                ["Outbox:PollingIntervalMs"] = "1000",
                ["Outbox:MaxReintentos"] = "3",
                ["Outbox:BatchSize"] = "20"
            })
            .Build();

        var servicios = new ServiceCollection();

        servicios.AddLogging();
        servicios.AddDbContext<InventarioDbContext>(opciones => opciones.UseNpgsql(cadenaConexion));
        servicios.AddOutbox<InventarioDbContext>(configuracion);
        servicios.Configure<InventarioOptions>(configuracion.GetSection(InventarioOptions.Seccion));

        servicios.AddScoped<IRepositorioExistencias, RepositorioExistencias>();
        servicios.AddScoped<IRepositorioReservas, RepositorioReservas>();
        servicios.AddScoped<IRepositorioMovimientos, RepositorioMovimientos>();
        servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
        servicios.AddScoped<IGeneradorCorrelativoReserva, GeneradorCorrelativoReserva>();
        servicios.AddScoped<IRepositorioReservasCarrito, RepositorioReservasCarrito>();
        servicios.AddScoped<ServicioReservas>();
        servicios.AddScoped<ServicioExistencias>();
        servicios.AddScoped<ServicioExpiracionReservas>();

        return servicios.BuildServiceProvider();
    }
}
