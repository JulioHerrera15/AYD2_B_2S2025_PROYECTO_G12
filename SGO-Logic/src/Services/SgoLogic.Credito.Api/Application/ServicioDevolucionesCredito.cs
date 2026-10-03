using System.Globalization;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

public enum ResultadoDevolucionCredito
{
    /// <summary>La orden no es a credito: la devolucion la reembolsa Pagos.</summary>
    Ignorada,

    Aplicada,

    /// <summary>Esa devolucion ya se habia aplicado (idempotencia por devolucionId).</summary>
    YaAplicada,

    /// <summary>No hay cuenta por cobrar para la orden o el monto no es valido.</summary>
    SinCuenta
}

/// <summary>
/// Devolucion de una compra a credito (DevolucionRegistrada con medioPago CREDITO).
/// Idempotente por devolucionId (movimiento DEVOLUCION unico). En una sola transaccion:
/// <list type="bullet">
///   <item>Descuenta del saldo pendiente lo que alcance y lo libera en la linea.</item>
///   <item>Si lo devuelto supera el saldo, el excedente ya lo pago el cliente: pide su
///   reembolso sobre los abonos aplicados, del mas reciente al mas antiguo.</item>
///   <item>Con saldo en 0 la cuenta se cierra (ANULADA por devolucion total sin pagos;
///   si no, PAGADA sin contar como pago puntual ni tardio).</item>
///   <item>Si la cuenta estaba VENCIDA, actualiza la mora (quita la pausa si ya no quedan
///   vencidas) y recalcula el perfil.</item>
///   <item>Registra el movimiento y emite DevolucionCreditoAplicada.</item>
/// </list>
/// Guarda por su cuenta (reintenta ante choque de Version de la cuenta o la linea), como
/// el resto de servicios que consume <c>credito.ordenes</c>.
/// </summary>
public sealed class ServicioDevolucionesCredito
{
    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly ServicioPerfilCrediticio _perfiles;
    private readonly ILogger<ServicioDevolucionesCredito> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioDevolucionesCredito(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ServicioPerfilCrediticio perfiles,
        ILogger<ServicioDevolucionesCredito> logger)
        : this(repositorio, outbox, perfiles, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioDevolucionesCredito(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        ServicioPerfilCrediticio perfiles,
        ILogger<ServicioDevolucionesCredito> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _perfiles = perfiles;
        _logger = logger;
        _reloj = reloj;
    }

    public async Task<ResultadoDevolucionCredito> ProcesarAsync(DevolucionRegistradaData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        if (datos.MedioPago != MedioPago.CREDITO)
        {
            return ResultadoDevolucionCredito.Ignorada;
        }

        for (var intento = 1; ; intento++)
        {
            try
            {
                return await ProcesarUnaVezAsync(datos, cancelacion);
            }
            catch (ExcepcionConcurrenciaCredito ex) when (intento < ServicioEvaluacionCredito.MaximoIntentos)
            {
                _logger.LogInformation(
                    ex,
                    "Conflicto de concurrencia al aplicar la devolucion {DevolucionId} (intento {Intento}); se reintenta.",
                    datos.DevolucionId, intento);
                _repositorio.DescartarCambios();
            }
            catch (ExcepcionDuplicadoCredito)
            {
                // Otra entrega de la misma devolucion gano la carrera: ya esta aplicada.
                _repositorio.DescartarCambios();
                return ResultadoDevolucionCredito.YaAplicada;
            }
        }
    }

    private async Task<ResultadoDevolucionCredito> ProcesarUnaVezAsync(DevolucionRegistradaData datos, CancellationToken cancelacion)
    {
        var devolucionId = datos.DevolucionId.Trim();
        if (await _repositorio.ExisteMovimientoAsync(TipoMovimientoCuenta.DEVOLUCION, devolucionId, cancelacion))
        {
            _logger.LogInformation("La devolucion {DevolucionId} ya se habia aplicado; no se repite.", devolucionId);
            return ResultadoDevolucionCredito.YaAplicada;
        }

        var cuenta = await _repositorio.ObtenerCuentaPorOrdenAsync(datos.OrdenOriginalId.Trim(), cancelacion);
        if (cuenta is null)
        {
            _logger.LogWarning(
                "Devolucion {DevolucionId} a credito de la orden {OrdenId} sin cuenta por cobrar; no hay deuda que ajustar.",
                devolucionId, datos.OrdenOriginalId);
            return ResultadoDevolucionCredito.SinCuenta;
        }

        if (!decimal.TryParse(datos.MontoReembolso, NumberStyles.Number, CultureInfo.InvariantCulture, out var monto) || monto <= 0m)
        {
            _logger.LogWarning(
                "Devolucion {DevolucionId} con monto invalido '{Monto}'; no se aplica.", devolucionId, datos.MontoReembolso);
            return ResultadoDevolucionCredito.SinCuenta;
        }

        var ahora = _reloj();
        var estabaVencida = cuenta.Estado == EstadoCuentaPorCobrar.VENCIDA;
        var huboPagos = await _repositorio.HayAbonosCobradosAsync(cuenta.Id, cancelacion);

        var descontado = cuenta.AplicarDevolucion(monto, devolucionTotal: !datos.EsParcial, huboPagos, ahora);

        var linea = await _repositorio.ObtenerLineaAsync(cuenta.ClienteId, cancelacion);
        linea?.Liberar(descontado, ahora);

        var porReembolsar = await SolicitarReembolsoExcedenteAsync(cuenta, monto - descontado, ahora, cancelacion);

        _repositorio.AgregarMovimiento(new MovimientoCuenta(
            cuenta.Id, cuenta.ClienteId, TipoMovimientoCuenta.DEVOLUCION, monto, devolucionId, ahora, descontado, porReembolsar));

        if (estabaVencida)
        {
            await _perfiles.ActualizarMoraYPerfilAsync(cuenta.ClienteId, cancelacion);
        }

        _outbox.Encolar(
            DevolucionCreditoAplicada.EventType,
            DevolucionCreditoAplicada.AggregateType,
            cuenta.Id.ToString("N"),
            cuenta.Version,
            TopologiaMensajeria.ExchangeEventos,
            DevolucionCreditoAplicada.RoutingKey,
            new DevolucionCreditoAplicadaData(
                devolucionId, cuenta.OrdenId, cuenta.ClienteId, cuenta.Moneda, descontado, porReembolsar, ahora));

        await _repositorio.GuardarCambiosAsync(cancelacion);

        MetricasCredito.DevolucionesTotal.Inc();
        _logger.LogInformation(
            "Devolucion {DevolucionId} aplicada a la cuenta {CuentaId}: {Descontado} menos de deuda, {Reembolso} por reembolsar, cuenta {Estado}.",
            devolucionId, cuenta.Id, descontado, porReembolsar, cuenta.Estado);
        return ResultadoDevolucionCredito.Aplicada;
    }

    /// <summary>
    /// Reparte el excedente sobre los abonos aplicados de la cuenta, del mas reciente al
    /// mas antiguo, sin pedir dos veces el mismo dinero. Devuelve lo que se pidio.
    /// </summary>
    private async Task<decimal> SolicitarReembolsoExcedenteAsync(
        CuentaPorCobrar cuenta, decimal excedente, DateTime ahora, CancellationToken cancelacion)
    {
        if (excedente <= 0m)
        {
            return 0m;
        }

        var solicitado = 0m;
        var abonos = (await _repositorio.ListarAbonosAplicadosAsync(cuenta.Id, cancelacion))
            .OrderByDescending(abono => abono.ResueltoEn ?? abono.SolicitadoEn)
            .ThenByDescending(abono => abono.Referencia, StringComparer.Ordinal);

        foreach (var abono in abonos)
        {
            var parte = Math.Min(excedente - solicitado, abono.PendientePorSolicitarReembolso);
            if (parte <= 0m)
            {
                continue;
            }

            SolicitudesReembolsoAbono.Solicitar(_outbox, abono, parte, MotivosReembolsoAbono.Devolucion, ahora);
            solicitado += parte;
            if (solicitado >= excedente)
            {
                break;
            }
        }

        if (solicitado < excedente)
        {
            _logger.LogWarning(
                "La devolucion sobre la cuenta {CuentaId} deja {Faltante} sin abonos cobrados que reembolsar.",
                cuenta.Id, excedente - solicitado);
        }

        return solicitado;
    }
}
