using SgoLogic.Auditoria.Worker.Application;

namespace SgoLogic.Auditoria.Tests;

[Collection("auditoria")]
public sealed class InsercionYRecuperacionTests
{
    private readonly AuditoriaFixture _fixture;

    public InsercionYRecuperacionTests(AuditoriaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Inserta_el_envelope_completo_y_lo_recupera_por_transactionId()
    {
        var registrador = new RegistradorEventoAuditoria();
        var evento = EventosDePrueba.Crear(data: new { producto = "ABC-1", cantidad = 3 });

        await using (var contexto = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida()))
        {
            await registrador.RegistrarAsync(evento, contexto, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var lectura = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
        var servicio = new AuditoriaConsultaService(lectura);

        var pagina = await servicio.ObtenerPorTransaccionAsync(evento.TransactionId, 1, 20, CancellationToken.None);

        Assert.Equal(1, pagina.Total);
        var fila = Assert.Single(pagina.Items);

        Assert.Equal(evento.EventId, fila.EventId);
        Assert.Equal(evento.EventType, fila.EventType);
        Assert.Equal(evento.EventVersion, fila.EventVersion);
        Assert.Equal(evento.Sequence, fila.Sequence);
        Assert.Equal(evento.TransactionId, fila.TransactionId);
        Assert.Equal(evento.AggregateType, fila.AggregateType);
        Assert.Equal(evento.AggregateId, fila.AggregateId);
        Assert.Equal(evento.Source, fila.Source);
        Assert.Equal("ABC-1", fila.Data.GetProperty("producto").GetString());
        Assert.Equal(3, fila.Data.GetProperty("cantidad").GetInt32());
    }

    [Fact]
    public async Task Tambien_se_recupera_por_aggregateId()
    {
        var registrador = new RegistradorEventoAuditoria();
        var evento = EventosDePrueba.Crear();

        await using (var contexto = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida()))
        {
            await registrador.RegistrarAsync(evento, contexto, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var lectura = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
        var servicio = new AuditoriaConsultaService(lectura);

        var pagina = await servicio.ObtenerPorAgregadoAsync(evento.AggregateId, 1, 20, CancellationToken.None);

        Assert.Equal(1, pagina.Total);
        Assert.Equal(evento.EventId, Assert.Single(pagina.Items).EventId);
    }
}