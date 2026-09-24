using SgoLogic.Auditoria.Worker.Application;

namespace SgoLogic.Auditoria.Tests;

/// <summary>
/// Auditoria no puede romperse porque otro equipo agregue un evento nuevo: como
/// consume con TData = JsonElement, cualquier forma de "data" se persiste igual,
/// sin que exista una clase concreta en Contracts para ese EventType.
/// </summary>
[Collection("auditoria")]
public sealed class EventoDesconocidoTests
{
    private readonly AuditoriaFixture _fixture;

    public EventoDesconocidoTests(AuditoriaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Un_evento_con_data_de_tipo_desconocido_para_Contracts_se_persiste_igual()
    {
        var registrador = new RegistradorEventoAuditoria();
        var evento = EventosDePrueba.Crear(
            eventType: "ContextoInventado.CosaQueNadieConoce",
            source: "contexto-inventado",
            data: new
            {
                campoQueNoExisteEnNingunEventoDeContracts = "valor-raro",
                anidado = new { numero = 42, lista = new[] { 1, 2, 3 } }
            });

        await using var contexto = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
        await registrador.RegistrarAsync(evento, contexto, CancellationToken.None);
        await contexto.SaveChangesAsync();

        await using var lectura = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
        var servicio = new AuditoriaConsultaService(lectura);
        var pagina = await servicio.ObtenerPorTransaccionAsync(evento.TransactionId, 1, 20, CancellationToken.None);

        var fila = Assert.Single(pagina.Items);
        Assert.Equal("ContextoInventado.CosaQueNadieConoce", fila.EventType);
        Assert.Equal(
            "valor-raro",
            fila.Data.GetProperty("campoQueNoExisteEnNingunEventoDeContracts").GetString());
        Assert.Equal(42, fila.Data.GetProperty("anidado").GetProperty("numero").GetInt32());
    }
}