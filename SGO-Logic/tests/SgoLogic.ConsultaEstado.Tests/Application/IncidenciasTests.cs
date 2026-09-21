using Microsoft.EntityFrameworkCore;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;
using Xunit;

namespace SgoLogic.ConsultaEstado.Tests.Application;

public sealed class IncidenciasTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ConsultaEstadoDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<ConsultaEstadoDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new ConsultaEstadoDbContext(options);
    }

    [Fact]
    public async Task Crear_incidencia_inicializa_con_estado_ABIERTA_y_orden_correcta()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-INC-001";

        var incidencia = new Incidencia
        {
            Id = Guid.NewGuid(),
            OrdenId = ordenId,
            Descripcion = "Paquete llegó con empaque roto",
            Estado = "ABIERTA",
            CreadaEn = DateTime.UtcNow
        };

        ctx.Incidencias.Add(incidencia);
        await ctx.SaveChangesAsync();

        var guardada = await ctx.Incidencias.FirstOrDefaultAsync(i => i.OrdenId == ordenId);
        Assert.NotNull(guardada);
        Assert.Equal("ABIERTA", guardada.Estado);
        Assert.Null(guardada.Resolucion);
        Assert.Equal("Paquete llegó con empaque roto", guardada.Descripcion);
    }

    [Fact]
    public async Task Actualizar_incidencia_cambia_estado_a_RESUELTA_y_guarda_resolucion()
    {
        using var ctx = CrearContexto();
        var id = Guid.NewGuid();

        var incidencia = new Incidencia
        {
            Id = id,
            OrdenId = "ORD-INC-002",
            Descripcion = "Demora en despacho",
            Estado = "ABIERTA",
            CreadaEn = DateTime.UtcNow
        };
        ctx.Incidencias.Add(incidencia);
        await ctx.SaveChangesAsync();

        // Actualización
        incidencia.Estado = "RESUELTA";
        incidencia.Resolucion = "Se reprogramó entrega prioritaria y se bonificó cupón de descuento.";
        incidencia.ActualizadaEn = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        var actualizada = await ctx.Incidencias.FindAsync(id);
        Assert.NotNull(actualizada);
        Assert.Equal("RESUELTA", actualizada.Estado);
        Assert.NotNull(actualizada.Resolucion);
        Assert.NotNull(actualizada.ActualizadaEn);
    }

    [Fact]
    public async Task Multiples_incidencias_para_misma_orden_se_listan_correctamente()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-MULTI-INC";

        ctx.Incidencias.AddRange(
            new Incidencia { Id = Guid.NewGuid(), OrdenId = ordenId, Descripcion = "Incidencia 1", Estado = "ABIERTA", CreadaEn = DateTime.UtcNow.AddHours(-2) },
            new Incidencia { Id = Guid.NewGuid(), OrdenId = ordenId, Descripcion = "Incidencia 2", Estado = "RESUELTA", Resolucion = "Solucionado", CreadaEn = DateTime.UtcNow.AddHours(-1) },
            new Incidencia { Id = Guid.NewGuid(), OrdenId = "OTRA-ORDEN", Descripcion = "Otra", Estado = "ABIERTA", CreadaEn = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var lista = await ctx.Incidencias
            .Where(i => i.OrdenId == ordenId)
            .OrderBy(i => i.CreadaEn)
            .ToListAsync();

        Assert.Equal(2, lista.Count);
        Assert.Equal("Incidencia 1", lista[0].Descripcion);
        Assert.Equal("Incidencia 2", lista[1].Descripcion);
    }
}
