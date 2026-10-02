using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Un PostgreSQL real (Testcontainers) compartido por toda la coleccion, con el
/// esquema creado por la migracion de verdad, no por EnsureCreated: estas pruebas
/// existen para ejercitar el SQL que EF Core genera contra Postgres (esquema
/// credito, outbox en public, tokens de concurrencia).
///
/// A diferencia de SgoLogic.Credito.Tests, esta suite NECESITA un Docker disponible.
/// </summary>
public sealed class CreditoFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sgo_credito_integracion")
        .WithUsername("sgo_test")
        .WithPassword("sgo_test")
        .Build();

    public string CadenaConexion => Postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();

        await using var contexto = CreditoServicios.CrearDbContext(CadenaConexion);
        await contexto.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Postgres.DisposeAsync().AsTask();
}

[CollectionDefinition("credito-integracion")]
public sealed class CreditoCollection : ICollectionFixture<CreditoFixture>;
