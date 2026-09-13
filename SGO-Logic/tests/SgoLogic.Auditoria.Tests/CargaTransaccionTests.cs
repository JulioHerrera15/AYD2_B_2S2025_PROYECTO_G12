using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SgoLogic.Auditoria.Worker.Application;

namespace SgoLogic.Auditoria.Tests;

/// <summary>
/// Prueba de humo de EAC-03 (recuperar toda la evidencia de una transaccion en
/// menos de 1 segundo), con un volumen razonable (~1000 eventos) para verificar
/// que la consulta usa el indice por TransactionId y no hace un seq scan. La
/// prueba de carga real (concurrencia, volumen de produccion) la hace el dueno
/// de la historia aparte; esto solo evita una regresion obvia.
/// </summary>
[Collection("auditoria")]
public sealed class CargaTransaccionTests
{
    private const int CantidadEventos = 1000;

    private readonly AuditoriaFixture _fixture;

    public CargaTransaccionTests(AuditoriaFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Recupera_mil_eventos_de_una_transaccion_en_menos_de_un_segundo()
    {
        var transactionId = Guid.NewGuid().ToString();
        var registrador = new RegistradorEventoAuditoria();

        await using (var contexto = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida()))
        {
            for (var i = 0; i < CantidadEventos; i++)
            {
                var evento = EventosDePrueba.Crear(transactionId: transactionId, sequence: i + 1);
                await registrador.RegistrarAsync(evento, contexto, CancellationToken.None);
            }

            await contexto.SaveChangesAsync();
        }

        await using var lectura = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());

        var cronometro = Stopwatch.StartNew();
        var total = await lectura.EventosAuditoria
            .Where(e => e.TransactionId == transactionId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Sequence)
            .ToListAsync();
        cronometro.Stop();

        Assert.Equal(CantidadEventos, total.Count);
        Assert.True(
            cronometro.Elapsed < TimeSpan.FromSeconds(1),
            $"La consulta tardo {cronometro.Elapsed.TotalMilliseconds} ms, se esperaba menos de 1000 ms.");
    }
}