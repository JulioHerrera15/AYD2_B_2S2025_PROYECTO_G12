using Microsoft.EntityFrameworkCore;
using Npgsql;
using SgoLogic.Auditoria.Worker.Infrastructure;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace SgoLogic.Auditoria.Tests;

/// <summary>
/// Un Postgres real (Testcontainers) para toda la coleccion "auditoria". El
/// usuario del contenedor (sgo_test) es el propietario, igual que
/// POSTGRES_USER en docker-compose: corre las migraciones y despues aplica
/// PermisosAuditoriaAplicador para crear el rol restringido de aplicacion, tal
/// como lo hace Program.cs al arrancar. Los tests de permisos se conectan con
/// ese rol restringido, nunca con el propietario.
/// </summary>
public sealed class AuditoriaFixture : IAsyncLifetime
{
    public const string UsuarioApp = "auditoria_app_test";
    public const string PasswordApp = "auditoria_app_test_pw";

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sgo_auditoria_test")
        .WithUsername("sgo_test")
        .WithPassword("sgo_test")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());

        var cadenaPropietario = Postgres.GetConnectionString();

        var opciones = new DbContextOptionsBuilder<AuditoriaDbContext>()
            .UseNpgsql(cadenaPropietario)
            .Options;

        await using (var contexto = new AuditoriaDbContext(opciones))
        {
            await contexto.Database.MigrateAsync();
        }

        await PermisosAuditoriaAplicador.AplicarAsync(cadenaPropietario, UsuarioApp, PasswordApp, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }

    public string CadenaConexionPropietario() => Postgres.GetConnectionString();

    /// <summary>Misma base, pero con el rol restringido (solo INSERT+SELECT sobre eventos_auditoria).</summary>
    public string CadenaConexionAppRestringida()
    {
        var builder = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Username = UsuarioApp,
            Password = PasswordApp
        };

        return builder.ConnectionString;
    }

    public AuditoriaDbContext CrearContexto(string cadenaConexion)
    {
        var opciones = new DbContextOptionsBuilder<AuditoriaDbContext>()
            .UseNpgsql(cadenaConexion)
            .Options;

        return new AuditoriaDbContext(opciones);
    }
}

[CollectionDefinition("auditoria")]
public sealed class AuditoriaCollection : ICollectionFixture<AuditoriaFixture>;