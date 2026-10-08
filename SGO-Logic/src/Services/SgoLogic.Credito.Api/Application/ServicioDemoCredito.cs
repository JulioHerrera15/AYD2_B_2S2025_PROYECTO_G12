using Microsoft.Extensions.Logging;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Operaciones de demo y pruebas (solo con Credito:EndpointsInternos=true):
/// reinicio, fecha de vencimiento manual y procesamiento inmediato de vencimientos.
/// </summary>
public sealed class ServicioDemoCredito
{
    private readonly IRepositorioCredito _repositorio;
    private readonly ILogger<ServicioDemoCredito> _logger;
    private readonly Func<DateTime> _reloj;
    private readonly ServicioPerfilCrediticio? _perfiles;
    private readonly ServicioVencimientos? _vencimientos;

    public ServicioDemoCredito(
        IRepositorioCredito repositorio,
        ILogger<ServicioDemoCredito> logger,
        ServicioPerfilCrediticio? perfiles = null,
        ServicioVencimientos? vencimientos = null)
        : this(repositorio, logger, () => DateTime.UtcNow, perfiles, vencimientos)
    {
    }

    public ServicioDemoCredito(
        IRepositorioCredito repositorio,
        ILogger<ServicioDemoCredito> logger,
        Func<DateTime> reloj,
        ServicioPerfilCrediticio? perfiles = null,
        ServicioVencimientos? vencimientos = null)
    {
        _repositorio = repositorio;
        _logger = logger;
        _reloj = reloj;
        _perfiles = perfiles;
        _vencimientos = vencimientos;
    }

    /// <summary>
    /// Borra movimientos, abonos, cuentas y decisiones, deja todos los saldos utilizados en 0 y quita
    /// la pausa por mora. Despues recalcula los perfiles: sin cuentas ya no hay deuda
    /// vencida ni pagos propios que cuenten. Historiales y limites de politica no cambian.
    /// </summary>
    public async Task<ResultadoReinicioDemo> ReiniciarAsync(CancellationToken cancelacion)
    {
        var resultado = await _repositorio.ReiniciarDemoAsync(_reloj(), cancelacion);

        if (_perfiles is not null)
        {
            foreach (var cliente in await _repositorio.ListarClientesConHistorialAsync(cancelacion))
            {
                await _perfiles.RecalcularSinGuardarAsync(cliente, cancelacion);
            }

            await _repositorio.GuardarCambiosAsync(cancelacion);
        }

        _logger.LogWarning(
            "Reinicio de demo de credito: {Movimientos} movimientos, {Abonos} abonos, {Cuentas} cuentas y {Decisiones} decisiones borradas, {Lineas} lineas con saldo en 0.",
            resultado.MovimientosBorrados, resultado.AbonosBorrados, resultado.CuentasBorradas, resultado.DecisionesBorradas, resultado.LineasReiniciadas);

        return resultado;
    }

    /// <summary>VenceEn = ahora + dias de la cuenta de la orden, y permite un recordatorio nuevo. False si no existe.</summary>
    public async Task<bool> FijarVencimientoAsync(string ordenId, int diasDesdeHoy, CancellationToken cancelacion)
    {
        var cuenta = await _repositorio.ObtenerCuentaPorOrdenAsync(ordenId.Trim(), cancelacion);
        if (cuenta is null)
        {
            return false;
        }

        cuenta.FijarVencimiento(_reloj().AddDays(diasDesdeHoy));
        await _repositorio.GuardarCambiosAsync(cancelacion);

        _logger.LogWarning("Demo: la cuenta de la orden {OrdenId} ahora vence el {VenceEn:O}.", ordenId, cuenta.VenceEn);
        return true;
    }

    public Task<ResultadoVencimientos> ProcesarVencimientosAsync(CancellationToken cancelacion) =>
        _vencimientos?.ProcesarAsync(cancelacion)
        ?? Task.FromResult(new ResultadoVencimientos(0, 0));
}
