using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

public enum ResultadoAnulacion
{
    /// <summary>La orden no es a credito.</summary>
    Ignorada,

    /// <summary>La cuenta VIGENTE paso a ANULADA y el monto volvio al disponible.</summary>
    Anulada,

    /// <summary>La cuenta ya estaba anulada, o la orden se rechazo sin cargo: nada que hacer.</summary>
    SinEfecto,

    /// <summary>Aun no habia decision: se registro ANULADA_SIN_CARGO para rechazar la evaluacion tardia.</summary>
    RegistradaSinCargo
}

/// <summary>
/// Compensacion del cargo cuando una orden a credito se cancela (OrdenCancelada con
/// medioPago CREDITO, sea por el cliente, por reserva vencida, por rechazo o por
/// timeout de la saga). Idempotente: anular dos veces no libera dos veces.
/// </summary>
public sealed class ServicioAnulacionCargos
{
    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly PoliticaCredito _politica;
    private readonly ILogger<ServicioAnulacionCargos> _logger;
    private readonly Func<DateTime> _reloj;
    private readonly ServicioPerfilCrediticio? _perfiles;

    public ServicioAnulacionCargos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        PoliticaCredito politica,
        ILogger<ServicioAnulacionCargos> logger,
        ServicioPerfilCrediticio? perfiles = null)
        : this(repositorio, outbox, politica, logger, () => DateTime.UtcNow, perfiles)
    {
    }

    /// <param name="perfiles">
    /// Para quitar la pausa por mora si la cuenta anulada era la vencida. Sin el (pruebas
    /// de bloque 2) la anulacion funciona igual pero no toca la mora.
    /// </param>
    public ServicioAnulacionCargos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        PoliticaCredito politica,
        ILogger<ServicioAnulacionCargos> logger,
        Func<DateTime> reloj,
        ServicioPerfilCrediticio? perfiles = null)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _politica = politica;
        _logger = logger;
        _reloj = reloj;
        _perfiles = perfiles;
    }

    public async Task<ResultadoAnulacion> ProcesarCancelacionAsync(OrdenCanceladaData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        if (datos.MedioPago != MedioPago.CREDITO)
        {
            return ResultadoAnulacion.Ignorada;
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
                    "Conflicto de concurrencia al anular el cargo de la orden {OrdenId} (intento {Intento}); se reintenta.",
                    datos.OrdenId, intento);
                _repositorio.DescartarCambios();
            }
        }
    }

    private async Task<ResultadoAnulacion> ProcesarUnaVezAsync(OrdenCanceladaData datos, CancellationToken cancelacion)
    {
        var ahora = _reloj();
        var ordenId = datos.OrdenId.Trim();
        var clienteId = ServicioPerfilCrediticio.NormalizarCliente(datos.ClienteId);

        var cuenta = await _repositorio.ObtenerCuentaPorOrdenAsync(ordenId, cancelacion);
        if (cuenta is not null)
        {
            if (cuenta.Estado is not (EstadoCuentaPorCobrar.VIGENTE or EstadoCuentaPorCobrar.VENCIDA))
            {
                return ResultadoAnulacion.SinEfecto;
            }

            var motivo = $"Orden cancelada ({datos.Motivo})";
            var liberado = cuenta.Anular(motivo, ahora);

            // Solo vuelve al disponible lo que faltaba pagar; lo ya abonado queda
            // pendiente de reembolso (bloque 4).
            var linea = await _repositorio.ObtenerLineaAsync(cuenta.ClienteId, cancelacion);
            linea?.Liberar(liberado, ahora);

            foreach (var abono in await _repositorio.ListarAbonosAplicadosAsync(cuenta.Id, cancelacion))
            {
                abono.MarcarReembolsoPendiente(null, null, null, ahora);
            }

            if (_perfiles is not null)
            {
                await _perfiles.ActualizarMoraYPerfilAsync(cuenta.ClienteId, cancelacion);
            }

            _outbox.Encolar(
                CargoCreditoAnulado.EventType,
                CargoCreditoAnulado.AggregateType,
                ordenId,
                cuenta.Version,
                TopologiaMensajeria.ExchangeEventos,
                CargoCreditoAnulado.RoutingKey,
                new CargoCreditoAnuladoData(
                    ordenId,
                    cuenta.ClienteId,
                    cuenta.Id.ToString("N"),
                    liberado,
                    cuenta.Moneda,
                    motivo,
                    ahora));

            await _repositorio.GuardarCambiosAsync(cancelacion);

            MetricasCredito.CargosAnuladosTotal.Inc();
            _logger.LogInformation(
                "Cargo {CargoId} de la orden {OrdenId} anulado; se devolvieron {Monto} al disponible de {ClienteId}.",
                cuenta.Id, ordenId, liberado, cuenta.ClienteId);
            return ResultadoAnulacion.Anulada;
        }

        var decision = await _repositorio.ObtenerDecisionAsync(ordenId, cancelacion);
        if (decision is not null)
        {
            // Rechazada (sin cargo) o ya marcada: no hay cupo que devolver.
            return ResultadoAnulacion.SinEfecto;
        }

        _repositorio.AgregarDecision(DecisionCredito.AnuladaSinCargo(
            ordenId, clienteId, _politica.Opciones.VersionPolitica, ahora));
        await _repositorio.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "La orden {OrdenId} se cancelo antes de evaluarse el credito; una evaluacion tardia se rechazara sin cargo.",
            ordenId);
        return ResultadoAnulacion.RegistradaSinCargo;
    }
}
