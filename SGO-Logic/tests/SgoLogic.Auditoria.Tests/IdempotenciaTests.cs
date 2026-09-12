using Microsoft.EntityFrameworkCore;
using SgoLogic.Auditoria.Worker.Application;

namespace SgoLogic.Auditoria.Tests;

[Collection("auditoria")]
public sealed class IdempotenciaTests
{
    private readonly AuditoriaFixture _fixture;

    public IdempotenciaTests(AuditoriaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task El_mismo_evento_entregado_dos_veces_se_registra_una_sola_vez()
    {
        var registrador = new RegistradorEventoAuditoria();
        var evento = EventosDePrueba.Crear();

        await using (var primeraEntrega = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida()))
        {
            await registrador.RegistrarAsync(evento, primeraEntrega, CancellationToken.None);
            await primeraEntrega.SaveChangesAsync();
        }

        await using (var segundaEntrega = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida()))
        {
            await registrador.RegistrarAsync(evento, segundaEntrega, CancellationToken.None);
            await segundaEntrega.SaveChangesAsync();
        }

        await using var verificacion = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
        var veces = await verificacion.EventosAuditoria.CountAsync(e => e.EventId == evento.EventId);

        Assert.Equal(1, veces);
    }
}