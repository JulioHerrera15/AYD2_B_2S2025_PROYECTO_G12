using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

public sealed record ResultadoVencimientos(int Recordadas, int Vencidas);

/// <summary>
/// Recordatorios y vencimientos de cuentas por cobrar. Logica sin BackgroundService
/// (la invoca VencimientosCreditoWorker cada Credito:Cobranza:IntervaloSegundos y el
/// endpoint interno de demo). Idempotente:
/// <list type="bullet">
///   <item>Recordatorio: cuentas VIGENTE con saldo que vencen en DiasRecordatorio o
///   menos, sin recordatorio previo. Emite CuentaCreditoPorVencer una sola vez.</item>
///   <item>Vencimiento: cuentas VIGENTE con saldo y VenceEn pasado. Pasan a VENCIDA, la
///   linea queda en pausa, se recalcula el perfil y se emite CuentaCreditoVencida.</item>
/// </list>
/// Sin intereses ni recargos: la mora solo pausa la linea y afecta el puntaje. Cada
/// cuenta se guarda por separado; si choca con otra operacion se deja para el
/// siguiente ciclo.
/// </summary>
public sealed class ServicioVencimientos
{
    private const int LotePorCiclo = 200;

    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly ServicioPerfilCrediticio _perfiles;
    private readonly CobranzaOptions _opciones;
    private readonly ILogger<ServicioVencimientos> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioVencimientos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ServicioPerfilCrediticio perfiles,
        IOptions<CobranzaOptions> opciones,
        ILogger<ServicioVencimientos> logger)
        : this(repositorio, outbox, perfiles, opciones, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioVencimientos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ServicioPerfilCrediticio perfiles,
        IOptions<CobranzaOptions> opciones,
        ILogger<ServicioVencimientos> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _perfiles = perfiles;
        _opciones = opciones.Value;
        _logger = logger;
        _reloj = reloj;
    }

    public async Task<ResultadoVencimientos> ProcesarAsync(CancellationToken cancelacion)
    {
        var ahora = _reloj();
        var recordadas = 0;
        var vencidas = 0;

        var limiteRecordatorio = ahora.AddDays(_opciones.DiasRecordatorio);
        foreach (var id in await _repositorio.ListarCuentasParaRecordatorioAsync(limiteRecordatorio, LotePorCiclo, cancelacion))
        {
            if (await IntentarAsync(id, cuenta => Recordar(cuenta, ahora, limiteRecordatorio), cancelacion))
            {
                recordadas++;
                MetricasCredito.RecordatoriosTotal.Inc();
            }
        }

        foreach (var id in await _repositorio.ListarCuentasPorVencerseAsync(ahora, LotePorCiclo, cancelacion))
        {
            if (await IntentarAsync(id, cuenta => VencerAsync(cuenta, ahora, cancelacion), cancelacion))
            {
                vencidas++;
                MetricasCredito.CuentasVencidasTotal.Inc();
            }
        }

        if (recordadas > 0 || vencidas > 0)
        {
            _logger.LogInformation(
                "Cobranza: {Recordadas} recordatorios emitidos y {Vencidas} cuentas vencidas.", recordadas, vencidas);
        }

        return new ResultadoVencimientos(recordadas, vencidas);
    }

    private Task<bool> Recordar(CuentaPorCobrar cuenta, DateTime ahora, DateTime limite)
    {
        if (cuenta.Estado != EstadoCuentaPorCobrar.VIGENTE || cuenta.SaldoPendiente <= 0m
            || cuenta.VenceEn < ahora || cuenta.VenceEn > limite || !cuenta.MarcarRecordatorio(ahora))
        {
            return Task.FromResult(false);
        }

        _outbox.Encolar(
            CuentaCreditoPorVencer.EventType,
            CuentaCreditoPorVencer.AggregateType,
            cuenta.Id.ToString("N"),
            cuenta.Version,
            TopologiaMensajeria.ExchangeEventos,
            CuentaCreditoPorVencer.RoutingKey,
            new CuentaCreditoPorVencerData(
                cuenta.Id.ToString("N"),
                cuenta.OrdenId,
                cuenta.NumeroOrden,
                cuenta.ClienteId,
                cuenta.SaldoPendiente,
                cuenta.Moneda,
                cuenta.VenceEn,
                Math.Max(0, (cuenta.VenceEn.Date - ahora.Date).Days)));
        return Task.FromResult(true);
    }

    private async Task<bool> VencerAsync(CuentaPorCobrar cuenta, DateTime ahora, CancellationToken cancelacion)
    {
        if (!cuenta.Vencer(ahora))
        {
            return false;
        }

        // La pausa de la linea y DEUDA_VENCIDA en el perfil salen del mismo calculo.
        await _perfiles.ActualizarMoraYPerfilAsync(cuenta.ClienteId, cancelacion);

        _outbox.Encolar(
            CuentaCreditoVencida.EventType,
            CuentaCreditoVencida.AggregateType,
            cuenta.Id.ToString("N"),
            cuenta.Version,
            TopologiaMensajeria.ExchangeEventos,
            CuentaCreditoVencida.RoutingKey,
            new CuentaCreditoVencidaData(
                cuenta.Id.ToString("N"),
                cuenta.OrdenId,
                cuenta.NumeroOrden,
                cuenta.ClienteId,
                cuenta.SaldoPendiente,
                cuenta.Moneda,
                cuenta.VenceEn,
                ahora));
        return true;
    }

    /// <summary>Carga la cuenta, aplica la accion y guarda. Ante choque de concurrencia descarta y sigue.</summary>
    private async Task<bool> IntentarAsync(Guid cuentaId, Func<CuentaPorCobrar, Task<bool>> accion, CancellationToken cancelacion)
    {
        try
        {
            var cuenta = await _repositorio.ObtenerCuentaAsync(cuentaId, cancelacion);
            if (cuenta is null || !await accion(cuenta))
            {
                return false;
            }

            await _repositorio.GuardarCambiosAsync(cancelacion);
            return true;
        }
        catch (ExcepcionConcurrenciaCredito ex)
        {
            _logger.LogInformation(ex, "La cuenta {CuentaId} cambio mientras se procesaba; se revisa en el siguiente ciclo.", cuentaId);
            _repositorio.DescartarCambios();
            return false;
        }
    }
}
