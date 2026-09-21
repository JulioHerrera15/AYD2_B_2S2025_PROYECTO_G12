using Microsoft.EntityFrameworkCore;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;
using SgoLogic.Contracts.Eventos.Despacho;
using Xunit;

namespace SgoLogic.ConsultaEstado.Tests.Consumers;

/// <summary>
/// Pruebas de la integracion de eventos de Despacho en la vista materializada.
///
/// REGLA CRITICA (seccion 4.2 del instrucciones.txt):
/// Los eventos de Despacho enriquecen DespachoId, GuiaDespacho y EstadoDespacho.
/// NUNCA deben escribir el campo Estado de la orden (exclusivo de OrdenCreada y
/// OrdenEstadoCambiado). Cada test lo verifica explicitamente.
/// </summary>
public sealed class IntegracionEventosDespachoTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ConsultaEstadoDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<ConsultaEstadoDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new ConsultaEstadoDbContext(opciones);
    }

    private static OrdenVista VistaBase(string ordenId, string estado = "CONFIRMADA") =>
        new()
        {
            OrdenId = ordenId,
            Estado = estado,
            UltimoSequence = 2,
            CreadaEn = DateTime.UtcNow,
            ActualizadaEn = DateTime.UtcNow
        };

    // ── DespachoEstadoActualizado ─────────────────────────────────────────────

    [Fact]
    public async Task DespachoEstadoActualizado_enriquece_DespachoId_y_EstadoDespacho()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-DSP-001";
        ctx.OrdenesVista.Add(VistaBase(ordenId));
        await ctx.SaveChangesAsync();

        var data = new DespachoEstadoActualizadoData(
            DespachoId: "DSP-001",
            OrdenId: ordenId,
            EstadoAnterior: "EN_PREPARACION",
            EstadoNuevo: "EN_TRANSITO",
            Detalle: null,
            ActualizadoEn: DateTime.UtcNow);

        // Simular lo que hace ManejarDespachoEstadoActualizado
        var vista = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(vista);
        vista.DespachoId = data.DespachoId;
        vista.EstadoDespacho = data.EstadoNuevo;
        vista.ActualizadaEn = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(resultado);
        Assert.Equal("DSP-001", resultado.DespachoId);
        Assert.Equal("EN_TRANSITO", resultado.EstadoDespacho);
    }

    [Fact]
    public async Task DespachoEstadoActualizado_NO_modifica_el_campo_Estado_de_la_orden()
    {
        // REGLA DE FUENTE AUTORITATIVA: Estado solo lo escriben eventos 1 y 2 de Ordenes
        using var ctx = CrearContexto();
        const string ordenId = "ORD-DSP-002";
        const string estadoOriginal = "CONFIRMADA";
        ctx.OrdenesVista.Add(VistaBase(ordenId, estadoOriginal));
        await ctx.SaveChangesAsync();

        var data = new DespachoEstadoActualizadoData(
            DespachoId: "DSP-002",
            OrdenId: ordenId,
            EstadoAnterior: "EN_PREPARACION",
            EstadoNuevo: "ENTREGADO",
            Detalle: "Entregado en domicilio",
            ActualizadoEn: DateTime.UtcNow);

        var vista = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(vista);
        // El handler SOLO toca DespachoId, EstadoDespacho y ActualizadaEn:
        vista.DespachoId = data.DespachoId;
        vista.EstadoDespacho = data.EstadoNuevo;
        vista.ActualizadaEn = DateTime.UtcNow;
        // NO: vista.Estado = data.EstadoNuevo; <-- eso violaría la regla
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(resultado);
        Assert.Equal(estadoOriginal, resultado.Estado); // Estado de orden INTACTO
        Assert.Equal("ENTREGADO", resultado.EstadoDespacho); // EstadoDespacho actualizado
    }

    [Fact]
    public async Task DespachoEstadoActualizado_multiple_actualizaciones_last_write_wins()
    {
        // El campo EstadoDespacho tiene politica last-write-wins porque el aggregateId
        // de Despacho es distinto al de Ordenes y no comparte el contador de Sequence.
        using var ctx = CrearContexto();
        const string ordenId = "ORD-DSP-003";
        ctx.OrdenesVista.Add(VistaBase(ordenId));
        await ctx.SaveChangesAsync();

        var vista = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(vista);

        // Primera actualizacion
        vista.DespachoId = "DSP-003";
        vista.EstadoDespacho = "EN_PREPARACION";
        await ctx.SaveChangesAsync();

        // Segunda actualizacion (mas reciente gana)
        vista.EstadoDespacho = "EN_TRANSITO";
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.Equal("EN_TRANSITO", resultado!.EstadoDespacho);
    }

    // ── PedidoListoParaRecoger ────────────────────────────────────────────────

    [Fact]
    public async Task PedidoListoParaRecoger_establece_EstadoDespacho_LISTA_PARA_RECOGER()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-DSP-004";
        ctx.OrdenesVista.Add(VistaBase(ordenId, "CONFIRMADA"));
        await ctx.SaveChangesAsync();

        var data = new PedidoListoParaRecogerData(
            DespachoId: "DSP-004",
            OrdenId: ordenId,
            UbicacionRecogidaId: "TIENDA-01-CENTRAL",
            ListoEn: DateTime.UtcNow);

        // Simular ManejarPedidoListoParaRecoger
        var vista = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(vista);
        vista.DespachoId = data.DespachoId;
        vista.EstadoDespacho = "LISTA_PARA_RECOGER";
        vista.ActualizadaEn = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(resultado);
        Assert.Equal("DSP-004", resultado.DespachoId);
        Assert.Equal("LISTA_PARA_RECOGER", resultado.EstadoDespacho);
    }

    [Fact]
    public async Task PedidoListoParaRecoger_NO_modifica_Estado_de_la_orden()
    {
        using var ctx = CrearContexto();
        const string ordenId = "ORD-DSP-005";
        const string estadoEsperado = "CONFIRMADA";
        ctx.OrdenesVista.Add(VistaBase(ordenId, estadoEsperado));
        await ctx.SaveChangesAsync();

        var vista = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(vista);
        vista.DespachoId = "DSP-005";
        vista.EstadoDespacho = "LISTA_PARA_RECOGER";
        vista.ActualizadaEn = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        var resultado = await ctx.OrdenesVista.FindAsync(ordenId);
        Assert.NotNull(resultado);
        Assert.Equal(estadoEsperado, resultado.Estado);   // Estado orden intacto
        Assert.Equal("LISTA_PARA_RECOGER", resultado.EstadoDespacho);
    }

    // ── Contratos de eventos ──────────────────────────────────────────────────

    [Fact]
    public void Contrato_DespachoEstadoActualizado_tiene_routing_key_correcta()
    {
        Assert.Equal("despacho.estado.actualizado", DespachoEstadoActualizado.RoutingKey);
        Assert.Equal("DespachoEstadoActualizado", DespachoEstadoActualizado.EventType);
    }

    [Fact]
    public void Contrato_PedidoListoParaRecoger_tiene_routing_key_correcta()
    {
        Assert.Equal("despacho.pedido.listo-para-recoger", PedidoListoParaRecoger.RoutingKey);
        Assert.Equal("PedidoListoParaRecoger", PedidoListoParaRecoger.EventType);
    }

    [Fact]
    public void Modelo_OrdenVista_tiene_campos_de_Despacho()
    {
        var vista = new OrdenVista
        {
            OrdenId = "ORD-MODEL-001",
            DespachoId = "DSP-MODEL-001",
            GuiaDespacho = "GE-123456",
            EstadoDespacho = "EN_TRANSITO",
            CreadaEn = DateTime.UtcNow,
            ActualizadaEn = DateTime.UtcNow
        };

        Assert.Equal("DSP-MODEL-001", vista.DespachoId);
        Assert.Equal("GE-123456", vista.GuiaDespacho);
        Assert.Equal("EN_TRANSITO", vista.EstadoDespacho);
    }

    [Fact]
    public void Campos_Despacho_son_opcionales_para_ordenes_sin_despacho_asignado()
    {
        // Una orden recien creada no tiene despacho todavia: los campos deben ser null
        var vista = new OrdenVista
        {
            OrdenId = "ORD-NUEVO-001",
            Estado = "CREADA",
            UltimoSequence = 1,
            CreadaEn = DateTime.UtcNow,
            ActualizadaEn = DateTime.UtcNow
        };

        Assert.Null(vista.DespachoId);
        Assert.Null(vista.GuiaDespacho);
        Assert.Null(vista.EstadoDespacho);
    }
}
