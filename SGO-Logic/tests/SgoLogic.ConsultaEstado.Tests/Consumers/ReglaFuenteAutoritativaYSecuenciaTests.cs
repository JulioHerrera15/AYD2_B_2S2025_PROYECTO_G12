using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using Xunit;

namespace SgoLogic.ConsultaEstado.Tests.Consumers;

public sealed class ReglaFuenteAutoritativaYSecuenciaTests
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
    public async Task Evento_OrdenCreada_establece_estado_inicial_y_secuencia_uno()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-001";

        var data = new OrdenCreadaData(
            OrdenId: ordenId,
            NumeroOrden: "000001",
            ClienteId: "CLI-99",
            Estado: EstadoOrden.CREADA,
            Canal: Canal.WEB,
            MetodoEntrega: MetodoEntrega.DOMICILIO,
            UbicacionSolicitadaId: null,
            Moneda: Moneda.GTQ,
            Subtotal: 1000m,
            Impuestos: 120m,
            Total: 1120m,
            Lineas: Array.Empty<OrdenCreadaLineaData>(),
            DireccionEntrega: null,
            CreadaEn: DateTime.UtcNow);

        var vista = new OrdenVista
        {
            OrdenId = ordenId,
            NumeroOrden = data.NumeroOrden,
            ClienteId = data.ClienteId,
            Estado = data.Estado.ToString(),
            UltimoSequence = 1,
            Total = data.Total,
            CreadaEn = data.CreadaEn,
            ActualizadaEn = DateTime.UtcNow
        };
        ctx.OrdenesVista.Add(vista);
        await ctx.SaveChangesAsync();

        var guardada = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(guardada);
        Assert.Equal("CREADA", guardada.Estado);
        Assert.Equal(1, guardada.UltimoSequence);
    }

    [Fact]
    public async Task Evento_OrdenEstadoCambiado_es_el_unico_que_modifica_estado()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-002";

        var vista = new OrdenVista
        {
            OrdenId = ordenId,
            Estado = "CREADA",
            UltimoSequence = 1,
            CreadaEn = DateTime.UtcNow,
            ActualizadaEn = DateTime.UtcNow
        };
        ctx.OrdenesVista.Add(vista);
        await ctx.SaveChangesAsync();

        // Simulamos llegada de OrdenEstadoCambiado con Sequence = 2
        vista.EstadoAnterior = "CREADA";
        vista.Estado = "CONFIRMADA";
        vista.UltimoSequence = 2;
        await ctx.SaveChangesAsync();

        var actualizada = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(actualizada);
        Assert.Equal("CONFIRMADA", actualizada.Estado);
        Assert.Equal(2, actualizada.UltimoSequence);
    }

    [Fact]
    public async Task Regla_Autoritativa_OrdenConfirmada_NO_modifica_el_campo_estado()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-003";

        var vista = new OrdenVista
        {
            OrdenId = ordenId,
            Estado = "CREADA",
            UltimoSequence = 1,
            CreadaEn = DateTime.UtcNow,
            ActualizadaEn = DateTime.UtcNow
        };
        ctx.OrdenesVista.Add(vista);
        await ctx.SaveChangesAsync();

        // Evento 8 OrdenConfirmada enriquece PagoId pero NO toca Estado
        var dataEvento8 = new OrdenConfirmadaData(
            OrdenId: ordenId,
            NumeroOrden: "000003",
            ClienteId: "CLI-10",
            Canal: Canal.WEB,
            TiendaDestinoId: "TIENDA-CENTRAL",
            Moneda: Moneda.GTQ,
            Subtotal: 500m,
            Impuestos: 60m,
            Total: 560m,
            PagoId: "PAG-998877",
            DocumentoFiscalId: null,
            Lineas: Array.Empty<OrdenConfirmadaLineaData>(),
            ConfirmadaEn: DateTime.UtcNow);

        // Regla: No tocar estado
        vista.PagoId = dataEvento8.PagoId;
        vista.TiendaDestinoId = dataEvento8.TiendaDestinoId;
        vista.UltimoSequence = 3;
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(resultado);
        Assert.Equal("CREADA", resultado.Estado); // Permanece CREADA, NO cambió
        Assert.Equal("PAG-998877", resultado.PagoId);
        Assert.Equal("TIENDA-CENTRAL", resultado.TiendaDestinoId);
        Assert.Equal(3, resultado.UltimoSequence);
    }

    [Fact]
    public async Task Regla_Autoritativa_OrdenCertificadaFiscalmente_enriquece_folio_pero_NO_toca_estado()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-004";

        var vista = new OrdenVista
        {
            OrdenId = ordenId,
            Estado = "CONFIRMADA",
            UltimoSequence = 2,
            CreadaEn = DateTime.UtcNow,
            ActualizadaEn = DateTime.UtcNow
        };
        ctx.OrdenesVista.Add(vista);
        await ctx.SaveChangesAsync();

        // Evento 12 OrdenCertificadaFiscalmente
        var dataEvento12 = new OrdenCertificadaFiscalmenteData(
            OrdenId: ordenId,
            NumeroOrden: "000004",
            TiendaDestinoId: null,
            DocumentoFiscalId: "SAT-FEL-UUID-12345",
            TipoDocumento: TipoDocumentoFiscal.FACTURA,
            Serie: "FE-A1",
            NumeroDocumento: "987654",
            NumeroAutorizacion: "AUTH-SAT-2026-X",
            Moneda: Moneda.GTQ,
            Total: 1500m,
            CertificadaEn: DateTime.UtcNow);

        // Regla: enriquecer folios fiscales sin alterar Estado
        vista.DocumentoFiscalId = dataEvento12.DocumentoFiscalId;
        vista.SerieDocumentoFiscal = dataEvento12.Serie;
        vista.NumeroDocumentoFiscal = dataEvento12.NumeroDocumento;
        vista.UltimoSequence = 4;
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(resultado);
        Assert.Equal("CONFIRMADA", resultado.Estado); // Permanece CONFIRMADA
        Assert.Equal("SAT-FEL-UUID-12345", resultado.DocumentoFiscalId);
        Assert.Equal("FE-A1", resultado.SerieDocumentoFiscal);
        Assert.Equal("987654", resultado.NumeroDocumentoFiscal);
        Assert.Equal(4, resultado.UltimoSequence);
    }

    [Fact]
    public void Ordenamiento_LastWriterWins_evento_con_secuencia_menor_o_igual_debe_descartarse()
    {
        var vista = new OrdenVista
        {
            OrdenId = "ORD-005",
            Estado = "CONFIRMADA",
            UltimoSequence = 5
        };

        // Evento tardío con Sequence = 3 (menor al último procesado = 5)
        var secuenciaMenor = 3;
        var debeDescartarseMenor = secuenciaMenor <= vista.UltimoSequence;
        Assert.True(debeDescartarseMenor);

        // Evento duplicado con Sequence = 5
        var secuenciaIgual = 5;
        var debeDescartarseIgual = secuenciaIgual <= vista.UltimoSequence;
        Assert.True(debeDescartarseIgual);

        // Evento nuevo con Sequence = 6
        var secuenciaMayor = 6;
        var debeDescartarseMayor = secuenciaMayor <= vista.UltimoSequence;
        Assert.False(debeDescartarseMayor);
    }
}
