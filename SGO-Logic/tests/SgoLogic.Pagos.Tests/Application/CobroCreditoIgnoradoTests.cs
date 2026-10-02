using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// Defensa en profundidad de la ruta de credito: un CobroSolicitado con medio de
/// pago CREDITO no llega a PayKlik ni crea un pago. Los demas medios no cambian.
/// </summary>
public sealed class CobroCreditoIgnoradoTests
{
    private static ServicioCobro Servicio(PagosDbContext contexto, ClientePayKlikFalso payklik, OutboxWriterFalso outbox) => new(
        contexto,
        payklik,
        outbox,
        Options.Create(new PayKlikOptions { MaxReintentos = 0, BackoffBaseMs = 0 }),
        Options.Create(new ReintentoOptions { ExpiracionMinutos = 60 }),
        BovedaPruebas.Cifrador,
        NullLogger<ServicioCobro>.Instance);

    private static PagosDbContext Contexto(string nombre) =>
        new(new DbContextOptionsBuilder<PagosDbContext>().UseInMemoryDatabase(nombre).Options);

    [Fact]
    public async Task CobroSolicitado_con_CREDITO_no_llama_a_PayKlik_ni_crea_pago()
    {
        var baseDatos = $"pagos-credito-{Guid.NewGuid()}";
        var payklik = ClientePayKlikFalso.Aprueba();
        var outbox = new OutboxWriterFalso();

        await using (var contexto = Contexto(baseDatos))
        {
            var resultado = await Servicio(contexto, payklik, outbox).ProcesarSolicitudAsync(
                new SolicitudCobro("ORD-2026-000901", "cliente1@sgo.local", Canal.WEB, Moneda.GTQ, 900m,
                    MedioPago.CREDITO, null, null, null),
                CancellationToken.None);
            await contexto.SaveChangesAsync();

            Assert.Null(resultado);
        }

        Assert.Empty(payklik.Peticiones);
        Assert.Empty(outbox.Eventos);
        await using var verificacion = Contexto(baseDatos);
        Assert.Empty(await verificacion.Pagos.ToListAsync());
    }

    [Fact]
    public async Task ProcesarSolicitud_con_TRANSFERENCIA_sigue_el_camino_de_siempre()
    {
        var payklik = ClientePayKlikFalso.Aprueba();
        await using var contexto = Contexto($"pagos-transferencia-{Guid.NewGuid()}");

        var pago = await Servicio(contexto, payklik, new OutboxWriterFalso()).ProcesarSolicitudAsync(
            new SolicitudCobro("ORD-2026-000902", "cliente1@sgo.local", Canal.WEB, Moneda.GTQ, 900m,
                MedioPago.TRANSFERENCIA, null, null, null),
            CancellationToken.None);

        Assert.NotNull(pago);
        Assert.Equal(MedioPago.TRANSFERENCIA, pago!.MedioPago);
    }
}
