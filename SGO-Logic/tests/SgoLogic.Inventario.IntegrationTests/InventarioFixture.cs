using Microsoft.EntityFrameworkCore;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;
using Testcontainers.PostgreSql;

namespace SgoLogic.Inventario.IntegrationTests;

/// <summary>
/// Un PostgreSQL real (Testcontainers) compartido por toda la coleccion, con el
/// esquema creado por las migraciones de verdad — no por EnsureCreated. Esa
/// distincion importa: estos tests existen justamente para ejercitar el SQL que
/// EF Core genera contra Postgres, incluida la secuencia
/// <c>inventario.reserva_correlativo</c> y los indices, que solo existen si corren
/// las migraciones.
///
/// A diferencia de los tests unitarios de <c>SgoLogic.Inventario.Tests</c>, esta
/// suite NECESITA un Docker disponible.
/// </summary>
public sealed class InventarioFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sgo_inventario_integracion")
        .WithUsername("sgo_test")
        .WithPassword("sgo_test")
        .Build();

    public string CadenaConexion => Postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();

        await using var contexto = InventarioServicios.CrearDbContext(CadenaConexion);
        await contexto.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Postgres.DisposeAsync().AsTask();
}

[CollectionDefinition("inventario-integracion")]
public sealed class InventarioCollection : ICollectionFixture<InventarioFixture>;
