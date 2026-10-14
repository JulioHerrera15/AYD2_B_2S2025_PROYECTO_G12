using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SgoLogic.Inventario.Api.Infrastructure.Persistencia;

namespace SgoLogic.Inventario.IntegrationTests;

/// <summary>
/// La migracion SucursalesPais agrega la columna <c>pais</c> y corrige las tiendas
/// T17 a T32, que la migracion Sucursales habia sembrado como departamentos de
/// Guatemala. Se prueba el caso del despliegue: una base que ya tenia aplicadas las
/// migraciones anteriores (con el catalogo viejo) y recibe solo la nueva.
///
/// Usa una base propia dentro del mismo contenedor para no depender del estado que
/// dejan las demas pruebas de la coleccion.
/// </summary>
[Collection("inventario-integracion")]
public sealed class MigracionSucursalesPaisTests
{
    private const string MigracionAnterior = "20261002000439_Productos";

    private readonly InventarioFixture _fixture;

    public MigracionSucursalesPaisTests(InventarioFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Sobre_una_base_con_el_catalogo_anterior_deja_cada_sucursal_en_su_pais()
    {
        var cadena = new NpgsqlConnectionStringBuilder(_fixture.CadenaConexion)
        {
            Database = $"sgo_inventario_migracion_{Guid.NewGuid():N}"[..40]
        }.ConnectionString;

        await using (var contexto = InventarioServicios.CrearDbContext(cadena))
        {
            await contexto.GetService<IMigrator>().MigrateAsync(MigracionAnterior);

            var nombreAnterior = await contexto.Database
                .SqlQueryRaw<string>("SELECT nombre AS \"Value\" FROM inventario.sucursal WHERE id = 'TIENDA-17-XELA'")
                .SingleAsync();
            Assert.Contains("Quetzaltenango", nombreAnterior);
        }

        await using (var contexto = InventarioServicios.CrearDbContext(cadena))
        {
            await contexto.Database.MigrateAsync();

            Assert.Empty(await contexto.Database.GetPendingMigrationsAsync());

            var sucursales = await contexto.Sucursales.AsNoTracking().ToListAsync();
            Assert.Equal(34, sucursales.Count);

            var porPais = sucursales
                .GroupBy(sucursal => sucursal.Pais)
                .ToDictionary(grupo => grupo.Key, grupo => grupo.Count());
            Assert.Equal(18, porPais[SucursalesSemilla.Guatemala]);
            Assert.Equal(8, porPais[SucursalesSemilla.ElSalvador]);
            Assert.Equal(8, porPais[SucursalesSemilla.CostaRica]);

            // La base migrada coincide fila por fila con el catalogo del codigo.
            foreach (var esperada in SucursalesSemilla.Todas)
            {
                var real = Assert.Single(sucursales, sucursal => sucursal.Id == esperada.Id);
                Assert.Equal(esperada.Pais, real.Pais);
                Assert.Equal(esperada.Nombre, real.Nombre);
                Assert.Equal(esperada.Departamento, real.Departamento);
                Assert.Equal(esperada.Municipio, real.Municipio);
                Assert.Equal(esperada.Latitud, real.Latitud);
                Assert.Equal(esperada.Longitud, real.Longitud);
            }

            var xela = Assert.Single(sucursales, sucursal => sucursal.Id == "TIENDA-17-XELA");
            Assert.Equal("MegaStore Multiplaza — San Salvador (T17)", xela.Nombre);

            await contexto.Database.EnsureDeletedAsync();
        }
    }
}
