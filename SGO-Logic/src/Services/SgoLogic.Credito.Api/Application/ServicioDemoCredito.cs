using Microsoft.Extensions.Logging;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Reinicio para repetir pruebas y demos (POST /interno/credito/demo/reiniciar):
/// borra cuentas por cobrar y decisiones y deja todos los saldos utilizados en 0.
/// Los perfiles, historiales y limites no cambian.
/// </summary>
public sealed class ServicioDemoCredito
{
    private readonly IRepositorioCredito _repositorio;
    private readonly ILogger<ServicioDemoCredito> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioDemoCredito(IRepositorioCredito repositorio, ILogger<ServicioDemoCredito> logger)
        : this(repositorio, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioDemoCredito(IRepositorioCredito repositorio, ILogger<ServicioDemoCredito> logger, Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _logger = logger;
        _reloj = reloj;
    }

    public async Task<ResultadoReinicioDemo> ReiniciarAsync(CancellationToken cancelacion)
    {
        var resultado = await _repositorio.ReiniciarDemoAsync(_reloj(), cancelacion);

        _logger.LogWarning(
            "Reinicio de demo de credito: {Cuentas} cuentas y {Decisiones} decisiones borradas, {Lineas} lineas con saldo en 0.",
            resultado.CuentasBorradas, resultado.DecisionesBorradas, resultado.LineasReiniciadas);

        return resultado;
    }
}
