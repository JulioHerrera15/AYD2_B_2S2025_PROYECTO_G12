using Microsoft.Extensions.Options;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>
/// Pruebas del disyuntor (circuit breaker) hacia PayKlik (ADR-009). No usan el
/// mock HTTP real: la "operacion" es un delegado que devuelve un valor o lanza una
/// de las fallas que cuentan (5xx, red, timeout).
/// </summary>
public sealed class DisyuntorPayKlikTests
{
    private static DisyuntorPayKlik Disyuntor(int aperturaSegundos = 1) => new(Options.Create(new PayKlikOptions
    {
        VentanaSegundos = 30,
        MinimoLlamadas = 3,
        RatioFallo = 0.5,
        AperturaSegundos = aperturaSegundos
    }));

    private static Task<int> Falla5xx(CancellationToken _) =>
        Task.FromException<int>(new PayKlikNoDisponibleException("PayKlik respondio 503."));

    private static async Task<int> InvocarFalla(DisyuntorPayKlik disyuntor)
    {
        try
        {
            return await disyuntor.EjecutarAsync(Falla5xx, CancellationToken.None);
        }
        catch (PayKlikNoDisponibleException)
        {
            return -1;
        }
    }

    [Fact]
    public async Task El_circuito_se_abre_tras_superar_el_ratio_de_fallo_con_el_minimo_de_llamadas()
    {
        var disyuntor = Disyuntor();

        for (var i = 0; i < 3; i++)
        {
            await InvocarFalla(disyuntor);
        }

        var salud = disyuntor.Salud();
        Assert.Equal("ABIERTO", salud.Estado);
        Assert.True(salud.FallosRecientes >= 3);
        Assert.NotNull(salud.UltimaApertura);
    }

    [Fact]
    public async Task Con_el_circuito_abierto_no_se_invoca_la_operacion()
    {
        var disyuntor = Disyuntor();
        for (var i = 0; i < 3; i++)
        {
            await InvocarFalla(disyuntor);
        }

        var invocada = false;

        await Assert.ThrowsAsync<CircuitoAbiertoException>(() => disyuntor.EjecutarAsync(
            _ =>
            {
                invocada = true;
                return Task.FromResult(0);
            },
            CancellationToken.None));

        Assert.False(invocada);
    }

    [Fact]
    public async Task Tras_la_apertura_pasa_a_semiabierto_y_cierra_ante_un_exito()
    {
        var disyuntor = Disyuntor(aperturaSegundos: 1);
        for (var i = 0; i < 3; i++)
        {
            await InvocarFalla(disyuntor);
        }

        Assert.Equal("ABIERTO", disyuntor.Salud().Estado);

        // Pasada la ventana de apertura, la siguiente llamada entra en semiabierto.
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        var resultado = await disyuntor.EjecutarAsync(_ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, resultado);
        Assert.Equal("CERRADO", disyuntor.Salud().Estado);
    }

    [Fact]
    public async Task Un_rechazo_de_negocio_no_abre_el_circuito()
    {
        var disyuntor = Disyuntor();

        // Un rechazo de negocio es una respuesta valida del proveedor: la operacion
        // retorna sin lanzar. El circuito lo ve como exito.
        for (var i = 0; i < 10; i++)
        {
            var resultado = await disyuntor.EjecutarAsync(
                _ => Task.FromResult(new ResultadoPayKlik(
                    null, EstadoRespuestaPayKlik.RECHAZADO, "51", "Fondos insuficientes",
                    "4242", "VISA", DateTime.UtcNow)),
                CancellationToken.None);

            Assert.Equal(EstadoRespuestaPayKlik.RECHAZADO, resultado.Estado);
        }

        var salud = disyuntor.Salud();
        Assert.Equal("CERRADO", salud.Estado);
        Assert.Equal(0, salud.FallosRecientes);
    }
}
