using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

public enum ResultadoCobroAbono
{
    Aplicado,
    Rechazado,
    Pendiente,

    /// <summary>Se cobro, pero la cuenta ya estaba anulada: se pidio su reembolso a Pagos.</summary>
    ReembolsoPendiente,

    /// <summary>El abono ya tenia un resultado final o no existe: no se hizo nada.</summary>
    Ignorado
}

/// <summary>
/// Aplica el resultado del cobro de un abono que publica Pagos. Idempotente por
/// abonoId: un abono que ya llego a un estado final no se vuelve a tocar.
///
/// Cobrado: baja el saldo de la cuenta y el saldo utilizado de la linea, emite
/// AbonoCreditoAplicado y, si la cuenta queda en 0, la marca PAGADA (puntual o tardia),
/// emite CuentaCreditoPagada, quita la pausa si ya no hay vencidas y recalcula el
/// perfil. Todo en un SaveChanges; ante choque de Version se relee y reintenta.
/// </summary>
public sealed class ServicioResultadoAbonos
{
    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly ServicioPerfilCrediticio _perfiles;
    private readonly ILogger<ServicioResultadoAbonos> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioResultadoAbonos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ServicioPerfilCrediticio perfiles,
        ILogger<ServicioResultadoAbonos> logger)
        : this(repositorio, outbox, perfiles, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioResultadoAbonos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ServicioPerfilCrediticio perfiles,
        ILogger<ServicioResultadoAbonos> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _perfiles = perfiles;
        _logger = logger;
        _reloj = reloj;
    }

    public Task<ResultadoCobroAbono> CobradoAsync(AbonoCreditoCobradoData datos, CancellationToken cancelacion) =>
        ConReintentoAsync(datos.AbonoId, () => CobradoUnaVezAsync(datos, cancelacion));

    public Task<ResultadoCobroAbono> RechazadoAsync(AbonoCreditoRechazadoData datos, CancellationToken cancelacion) =>
        ConReintentoAsync(datos.AbonoId, async () =>
        {
            var abono = await AbonoEnCursoAsync(datos.AbonoId, cancelacion);
            if (abono is null)
            {
                return ResultadoCobroAbono.Ignorado;
            }

            abono.MarcarRechazado(datos.CodigoRespuesta, _reloj());
            await _repositorio.GuardarCambiosAsync(cancelacion);
            MetricasCredito.AbonosTotal.WithLabels("rechazado").Inc();
            return ResultadoCobroAbono.Rechazado;
        });

    public Task<ResultadoCobroAbono> PendienteAsync(AbonoCreditoPendienteData datos, CancellationToken cancelacion) =>
        ConReintentoAsync(datos.AbonoId, async () =>
        {
            var abono = await AbonoEnCursoAsync(datos.AbonoId, cancelacion);
            if (abono is null)
            {
                return ResultadoCobroAbono.Ignorado;
            }

            abono.MarcarPendiente(datos.MotivoFalla, _reloj());
            await _repositorio.GuardarCambiosAsync(cancelacion);
            MetricasCredito.AbonosTotal.WithLabels("pendiente").Inc();
            return ResultadoCobroAbono.Pendiente;
        });

    private async Task<ResultadoCobroAbono> CobradoUnaVezAsync(AbonoCreditoCobradoData datos, CancellationToken cancelacion)
    {
        var abono = await AbonoEnCursoAsync(datos.AbonoId, cancelacion);
        if (abono is null)
        {
            return ResultadoCobroAbono.Ignorado;
        }

        var ahora = _reloj();
        var cuenta = await _repositorio.ObtenerCuentaAsync(abono.CuentaId, cancelacion);

        // La cuenta se anulo (orden cancelada) mientras se cobraba: el dinero entro pero
        // ya no hay deuda. No se toca la linea; se pide el reembolso completo del abono en
        // la misma transaccion.
        if (cuenta is null || !cuenta.EsPagable || abono.Monto > cuenta.SaldoPendiente)
        {
            abono.MarcarReembolsoPendiente(datos.AutorizacionId, datos.UltimosCuatro, datos.Marca, ahora);
            SolicitudesReembolsoAbono.SolicitarCompleto(_outbox, abono, cuenta, ahora);
            await _repositorio.GuardarCambiosAsync(cancelacion);
            MetricasCredito.AbonosTotal.WithLabels("reembolso_pendiente").Inc();
            _logger.LogWarning(
                "Abono {Referencia} cobrado sobre la cuenta {CuentaId} que ya no admite pagos; queda pendiente de reembolso.",
                abono.Referencia, abono.CuentaId);
            return ResultadoCobroAbono.ReembolsoPendiente;
        }

        var pagada = cuenta.AplicarAbono(abono.Monto, ahora);
        abono.MarcarAplicado(datos.AutorizacionId, datos.UltimosCuatro, datos.Marca, ahora);

        var linea = await _repositorio.ObtenerLineaAsync(cuenta.ClienteId, cancelacion);
        linea?.Liberar(abono.Monto, ahora);

        if (pagada)
        {
            _outbox.Encolar(
                CuentaCreditoPagada.EventType,
                CuentaCreditoPagada.AggregateType,
                cuenta.Id.ToString("N"),
                cuenta.Version,
                TopologiaMensajeria.ExchangeEventos,
                CuentaCreditoPagada.RoutingKey,
                new CuentaCreditoPagadaData(
                    cuenta.Id.ToString("N"), cuenta.OrdenId, cuenta.ClienteId, cuenta.PagadaEn!.Value, cuenta.PagoPuntual == true));

            // Pagar puede quitar la pausa (si era la ultima vencida) y cambia el
            // comportamiento propio (pago puntual o tardio): mora y perfil al dia.
            await _perfiles.ActualizarMoraYPerfilAsync(cuenta.ClienteId, cancelacion);
        }

        _outbox.Encolar(
            AbonoCreditoAplicado.EventType,
            AbonoCreditoAplicado.AggregateType,
            abono.Id.ToString("N"),
            abono.Version,
            TopologiaMensajeria.ExchangeEventos,
            AbonoCreditoAplicado.RoutingKey,
            new AbonoCreditoAplicadoData(
                abono.Id.ToString("N"),
                cuenta.ClienteId,
                cuenta.Id.ToString("N"),
                cuenta.OrdenId,
                abono.Monto,
                abono.Moneda,
                cuenta.SaldoPendiente,
                linea?.Disponible ?? 0m,
                ahora));

        await _repositorio.GuardarCambiosAsync(cancelacion);

        MetricasCredito.AbonosTotal.WithLabels("aplicado").Inc();
        _logger.LogInformation(
            "Abono {Referencia} aplicado a la cuenta {CuentaId}: saldo {Saldo}, pagada {Pagada}.",
            abono.Referencia, cuenta.Id, cuenta.SaldoPendiente, pagada);
        return ResultadoCobroAbono.Aplicado;
    }

    /// <summary>El abono si existe y sigue en curso; null si ya tiene resultado final (idempotencia).</summary>
    private async Task<AbonoCredito?> AbonoEnCursoAsync(string abonoId, CancellationToken cancelacion)
    {
        var abono = Guid.TryParse(abonoId, out var id) ? await _repositorio.ObtenerAbonoAsync(id, cancelacion) : null;
        if (abono is null)
        {
            _logger.LogWarning("Resultado de cobro para el abono {AbonoId} inexistente; se ignora.", abonoId);
            return null;
        }

        if (abono.EsFinal)
        {
            _logger.LogInformation(
                "El abono {Referencia} ya esta en {Estado}; el resultado repetido se ignora.", abono.Referencia, abono.Estado);
            return null;
        }

        return abono;
    }

    private async Task<ResultadoCobroAbono> ConReintentoAsync(string abonoId, Func<Task<ResultadoCobroAbono>> accion)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                return await accion();
            }
            catch (ExcepcionConcurrenciaCredito ex) when (intento < ServicioEvaluacionCredito.MaximoIntentos)
            {
                _logger.LogInformation(
                    ex, "Conflicto de concurrencia al aplicar el cobro del abono {AbonoId} (intento {Intento}); se reintenta.",
                    abonoId, intento);
                _repositorio.DescartarCambios();
            }
        }
    }
}
