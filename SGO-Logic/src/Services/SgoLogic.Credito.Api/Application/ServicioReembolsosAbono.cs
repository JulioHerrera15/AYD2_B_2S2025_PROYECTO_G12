using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

public enum ResultadoReembolsoAbono
{
    Aplicado,
    Fallido,

    /// <summary>El abono no existe o la confirmacion ya se habia aplicado.</summary>
    Ignorado
}

/// <summary>
/// Reembolso de abonos a credito:
/// <list type="bullet">
///   <item><see cref="SolicitarPendientesAsync" />: los abonos en REEMBOLSO_PENDIENTE que
///   aun no tienen solicitud (por ejemplo, los que quedaron asi antes de este bloque)
///   piden su reembolso completo. Lo invoca ReembolsosAbonoCreditoWorker.</item>
///   <item><see cref="ReembolsadoAsync" />: Pagos confirmo un reembolso; suma
///   MontoReembolsado y pasa el abono a REEMBOLSADO si quedo completo. Idempotente por
///   reembolsoId (movimiento REEMBOLSO_ABONO unico).</item>
///   <item><see cref="FallidoAsync" />: Pagos no pudo reembolsar; el abono queda marcado
///   para atencion manual.</item>
/// </list>
/// </summary>
public sealed class ServicioReembolsosAbono
{
    private const int LotePorCiclo = 200;

    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<ServicioReembolsosAbono> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioReembolsosAbono(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ILogger<ServicioReembolsosAbono> logger)
        : this(repositorio, outbox, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioReembolsosAbono(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ILogger<ServicioReembolsosAbono> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _logger = logger;
        _reloj = reloj;
    }

    /// <summary>Pide el reembolso de los REEMBOLSO_PENDIENTE sin solicitud. Devuelve cuantos pidio.</summary>
    public async Task<int> SolicitarPendientesAsync(CancellationToken cancelacion)
    {
        var solicitados = 0;
        foreach (var id in await _repositorio.ListarAbonosReembolsoSinSolicitudAsync(LotePorCiclo, cancelacion))
        {
            try
            {
                var abono = await _repositorio.ObtenerAbonoAsync(id, cancelacion);
                if (abono is null || abono.Estado != EstadoAbonoCredito.REEMBOLSO_PENDIENTE || abono.ReembolsoSolicitadoEn is not null)
                {
                    continue;
                }

                var cuenta = await _repositorio.ObtenerCuentaAsync(abono.CuentaId, cancelacion);
                if (!SolicitudesReembolsoAbono.SolicitarCompleto(_outbox, abono, cuenta, _reloj()))
                {
                    continue;
                }

                await _repositorio.GuardarCambiosAsync(cancelacion);
                solicitados++;
                _logger.LogInformation(
                    "Se pidio el reembolso del abono {Referencia} que estaba pendiente sin solicitud.", abono.Referencia);
            }
            catch (ExcepcionConcurrenciaCredito ex)
            {
                _logger.LogInformation(ex, "El abono {AbonoId} cambio mientras se pedia su reembolso; queda para el siguiente ciclo.", id);
                _repositorio.DescartarCambios();
            }
        }

        return solicitados;
    }

    public async Task<ResultadoReembolsoAbono> ReembolsadoAsync(AbonoCreditoReembolsadoData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        for (var intento = 1; ; intento++)
        {
            try
            {
                return await ReembolsadoUnaVezAsync(datos, cancelacion);
            }
            catch (ExcepcionConcurrenciaCredito ex) when (intento < ServicioEvaluacionCredito.MaximoIntentos)
            {
                _logger.LogInformation(
                    ex, "Conflicto de concurrencia al aplicar el reembolso {ReembolsoId} (intento {Intento}); se reintenta.",
                    datos.ReembolsoId, intento);
                _repositorio.DescartarCambios();
            }
            catch (ExcepcionDuplicadoCredito)
            {
                _repositorio.DescartarCambios();
                return ResultadoReembolsoAbono.Ignorado;
            }
        }
    }

    public async Task<ResultadoReembolsoAbono> FallidoAsync(ReembolsoAbonoCreditoFallidoData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        for (var intento = 1; ; intento++)
        {
            try
            {
                var abono = await AbonoAsync(datos.AbonoId, cancelacion);
                if (abono is null)
                {
                    return ResultadoReembolsoAbono.Ignorado;
                }

                abono.MarcarReembolsoFallido(datos.MotivoFalla, _reloj());
                await _repositorio.GuardarCambiosAsync(cancelacion);

                MetricasCredito.ReembolsosFallidosTotal.Inc();
                _logger.LogError(
                    "No se pudo reembolsar {Monto} del abono {Referencia} (reembolso {ReembolsoId}): {Motivo}. Requiere atencion manual.",
                    datos.Monto, abono.Referencia, datos.ReembolsoId, datos.MotivoFalla);
                return ResultadoReembolsoAbono.Fallido;
            }
            catch (ExcepcionConcurrenciaCredito ex) when (intento < ServicioEvaluacionCredito.MaximoIntentos)
            {
                _logger.LogInformation(
                    ex, "Conflicto de concurrencia al marcar el reembolso fallido {ReembolsoId} (intento {Intento}); se reintenta.",
                    datos.ReembolsoId, intento);
                _repositorio.DescartarCambios();
            }
        }
    }

    private async Task<ResultadoReembolsoAbono> ReembolsadoUnaVezAsync(AbonoCreditoReembolsadoData datos, CancellationToken cancelacion)
    {
        var abono = await AbonoAsync(datos.AbonoId, cancelacion);
        if (abono is null)
        {
            return ResultadoReembolsoAbono.Ignorado;
        }

        var reembolsoId = datos.ReembolsoId.Trim();
        if (await _repositorio.ExisteMovimientoAsync(TipoMovimientoCuenta.REEMBOLSO_ABONO, reembolsoId, cancelacion))
        {
            _logger.LogInformation("El reembolso {ReembolsoId} ya estaba aplicado; se ignora.", reembolsoId);
            return ResultadoReembolsoAbono.Ignorado;
        }

        var ahora = _reloj();
        var completo = abono.AplicarReembolso(datos.Monto, ahora);
        if (datos.Monto > 0m)
        {
            _repositorio.AgregarMovimiento(new MovimientoCuenta(
                abono.CuentaId, abono.ClienteId, TipoMovimientoCuenta.REEMBOLSO_ABONO, datos.Monto, reembolsoId, ahora,
                abonoId: abono.Id));
        }

        await _repositorio.GuardarCambiosAsync(cancelacion);

        MetricasCredito.ReembolsosAplicadosTotal.Inc();
        _logger.LogInformation(
            "Reembolso {ReembolsoId} de {Monto} aplicado al abono {Referencia}; reembolsado {Reembolsado} de {Cobrado}, completo {Completo}.",
            reembolsoId, datos.Monto, abono.Referencia, abono.MontoReembolsado, abono.Monto, completo);
        return ResultadoReembolsoAbono.Aplicado;
    }

    private async Task<AbonoCredito?> AbonoAsync(string abonoId, CancellationToken cancelacion)
    {
        var abono = Guid.TryParse(abonoId, out var id) ? await _repositorio.ObtenerAbonoAsync(id, cancelacion) : null;
        if (abono is null)
        {
            _logger.LogWarning("Resultado de reembolso para el abono {AbonoId} inexistente; se ignora.", abonoId);
        }

        return abono;
    }
}
