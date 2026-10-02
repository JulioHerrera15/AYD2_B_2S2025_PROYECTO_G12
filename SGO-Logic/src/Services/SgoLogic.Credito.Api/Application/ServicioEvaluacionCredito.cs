using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

public enum ResultadoEvaluacion
{
    Aprobada,
    Rechazada,

    /// <summary>La orden ya tenia una decision: no se hizo nada.</summary>
    YaDecidida
}

/// <summary>
/// Decide una compra a credito dentro de la saga de la orden. Idempotente por
/// OrdenId: la decision es unica por orden.
///
/// Aprobar es, en un solo SaveChanges: apartar el monto en la linea, emitir la
/// cuenta por cobrar, guardar la decision y encolar CreditoAprobado. Dos compras
/// simultaneas del mismo cliente compiten por la Version de la linea: la que pierde
/// recibe <see cref="ExcepcionConcurrenciaCredito" />, descarta lo pendiente y se
/// reevalua con el saldo releido, asi que entre las dos nunca superan el limite.
/// </summary>
public sealed class ServicioEvaluacionCredito
{
    public const int MaximoIntentos = 5;

    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly PoliticaCredito _politica;
    private readonly ILogger<ServicioEvaluacionCredito> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioEvaluacionCredito(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        PoliticaCredito politica,
        ILogger<ServicioEvaluacionCredito> logger)
        : this(repositorio, outbox, politica, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioEvaluacionCredito(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        PoliticaCredito politica,
        ILogger<ServicioEvaluacionCredito> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _politica = politica;
        _logger = logger;
        _reloj = reloj;
    }

    /// <exception cref="ArgumentException">La solicitud no trae orden, cliente o un monto positivo.</exception>
    public async Task<ResultadoEvaluacion> EvaluarAsync(EvaluacionCreditoSolicitadaData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (string.IsNullOrWhiteSpace(datos.OrdenId) || string.IsNullOrWhiteSpace(datos.ClienteId))
        {
            throw new ArgumentException("La solicitud de credito debe traer orden y cliente.", nameof(datos));
        }

        if (datos.Monto <= 0m)
        {
            throw new ArgumentException("El monto de la compra a credito debe ser mayor que 0.", nameof(datos));
        }

        for (var intento = 1; ; intento++)
        {
            try
            {
                return await EvaluarUnaVezAsync(datos, cancelacion);
            }
            catch (ExcepcionConcurrenciaCredito ex) when (intento < MaximoIntentos)
            {
                _logger.LogInformation(
                    ex,
                    "Conflicto de concurrencia al evaluar la orden {OrdenId} (intento {Intento}); se relee la linea y se reintenta.",
                    datos.OrdenId, intento);
                _repositorio.DescartarCambios();
            }
        }
    }

    private async Task<ResultadoEvaluacion> EvaluarUnaVezAsync(EvaluacionCreditoSolicitadaData datos, CancellationToken cancelacion)
    {
        var ahora = _reloj();
        var ordenId = datos.OrdenId.Trim();
        var clienteId = ServicioPerfilCrediticio.NormalizarCliente(datos.ClienteId);
        var opciones = _politica.Opciones;

        var existente = await _repositorio.ObtenerDecisionAsync(ordenId, cancelacion);
        if (existente is not null)
        {
            if (existente.Resultado != ResultadoDecisionCredito.ANULADA_SIN_CARGO)
            {
                _logger.LogInformation(
                    "La orden {OrdenId} ya tiene decision de credito {Resultado}; no se evalua de nuevo.",
                    ordenId, existente.Resultado);
                return ResultadoEvaluacion.YaDecidida;
            }

            // La orden se cancelo antes de que llegara esta evaluacion: se rechaza sin apartar cupo.
            existente.RechazarPorOrdenCancelada(datos.Monto, ahora);
            EncolarRechazo(existente, CatalogoMensajesCredito.OrdenCancelada());
            await _repositorio.GuardarCambiosAsync(cancelacion);
            MetricasCredito.EvaluacionesTotal.WithLabels("rechazada").Inc();
            return ResultadoEvaluacion.Rechazada;
        }

        var perfil = await _repositorio.ObtenerPerfilAsync(clienteId, cancelacion);
        var linea = await _repositorio.ObtenerLineaAsync(clienteId, cancelacion);

        var motivo = MotivoRechazo(perfil, linea, datos.Monto);
        if (motivo is not null)
        {
            var rechazo = DecisionCredito.Rechazada(
                ordenId, clienteId, datos.Monto, motivo, perfil, linea, opciones.VersionPolitica, ahora);
            _repositorio.AgregarDecision(rechazo);
            EncolarRechazo(rechazo, await MensajeParaAsync(motivo, perfil, linea, datos.Monto, clienteId, ahora, cancelacion));
            await _repositorio.GuardarCambiosAsync(cancelacion);

            MetricasCredito.EvaluacionesTotal.WithLabels("rechazada").Inc();
            _logger.LogInformation(
                "Credito rechazado para la orden {OrdenId} de {ClienteId}: {Motivo}.", ordenId, clienteId, motivo);
            return ResultadoEvaluacion.Rechazada;
        }

        var disponibleAntes = linea!.Disponible;
        linea.Cargar(datos.Monto, ahora);

        var cuenta = new CuentaPorCobrar(
            ordenId, datos.NumeroOrden, clienteId, datos.Monto, linea.Moneda, ahora, opciones.PlazoDias);
        var aprobacion = DecisionCredito.Aprobada(ordenId, clienteId, datos.Monto, perfil!, linea, disponibleAntes, ahora);
        _repositorio.AgregarCuenta(cuenta);
        _repositorio.AgregarDecision(aprobacion);

        _outbox.Encolar(
            CreditoAprobado.EventType,
            CreditoAprobado.AggregateType,
            ordenId,
            1,
            TopologiaMensajeria.ExchangeEventos,
            CreditoAprobado.RoutingKey,
            new CreditoAprobadoData(
                ordenId,
                clienteId,
                cuenta.Id.ToString("N"),
                aprobacion.Id.ToString("N"),
                datos.Monto,
                cuenta.Moneda,
                cuenta.VenceEn,
                linea.LimiteAprobado,
                linea.Disponible,
                ahora));

        await _repositorio.GuardarCambiosAsync(cancelacion);

        MetricasCredito.EvaluacionesTotal.WithLabels("aprobada").Inc();
        _logger.LogInformation(
            "Credito aprobado para la orden {OrdenId} de {ClienteId}: cargo {CargoId}, disponible restante {Disponible}.",
            ordenId, clienteId, cuenta.Id, linea.Disponible);
        return ResultadoEvaluacion.Aprobada;
    }

    /// <summary>Null si se aprueba; si no, el codigo del primer impedimento.</summary>
    private static string? MotivoRechazo(PerfilCrediticio? perfil, LineaCredito? linea, decimal monto)
    {
        if (perfil is null)
        {
            return CodigosMotivoCredito.SinHistorial;
        }

        if (!perfil.Elegible)
        {
            return perfil.CodigoMotivo ?? CodigosMotivoCredito.PuntajeInsuficiente;
        }

        if (linea?.Estado == EstadoLineaCredito.BLOQUEADA)
        {
            return CodigosMotivoCredito.LineaBloqueada;
        }

        if (linea is null || linea.Estado != EstadoLineaCredito.ACTIVA || linea.Disponible < monto)
        {
            return CodigosMotivoCredito.CupoInsuficiente;
        }

        return null;
    }

    private async Task<MensajeCredito> MensajeParaAsync(
        string motivo,
        PerfilCrediticio? perfil,
        LineaCredito? linea,
        decimal monto,
        string clienteId,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var opciones = _politica.Opciones;
        switch (motivo)
        {
            case CodigosMotivoCredito.CupoInsuficiente:
                return CatalogoMensajesCredito.CupoInsuficiente(linea?.Disponible ?? 0m, monto);

            case CodigosMotivoCredito.LineaBloqueada:
                return CatalogoMensajesCredito.LineaBloqueada();

            case CodigosMotivoCredito.AntiguedadInsuficiente:
            {
                var historial = await _repositorio.ObtenerHistorialAsync(clienteId, cancelacion);
                int? faltan = historial is null
                    ? null
                    : Math.Max(0, opciones.AntiguedadMinimaDias - historial.DiasComoCliente(ahora));
                return CatalogoMensajesCredito.NoElegible(motivo, opciones.AntiguedadMinimaDias, faltan, null);
            }

            default:
                FactorCliente? masDebil = perfil is null
                    ? null
                    : FactoresCredito.MasDebil(FactoresCredito.Desde(perfil, opciones));
                return CatalogoMensajesCredito.NoElegible(motivo, opciones.AntiguedadMinimaDias, null, masDebil);
        }
    }

    private void EncolarRechazo(DecisionCredito decision, MensajeCredito mensaje) =>
        _outbox.Encolar(
            CreditoRechazado.EventType,
            CreditoRechazado.AggregateType,
            decision.OrdenId,
            1,
            TopologiaMensajeria.ExchangeEventos,
            CreditoRechazado.RoutingKey,
            new CreditoRechazadoData(
                decision.OrdenId,
                decision.ClienteId,
                decision.Id.ToString("N"),
                decision.CodigoMotivo ?? CodigosMotivoCredito.CupoInsuficiente,
                CatalogoMensajesCredito.MensajeRechazo(mensaje),
                decision.DecididoEn));
}
