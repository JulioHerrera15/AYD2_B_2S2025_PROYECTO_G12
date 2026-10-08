using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Recalcula el perfil crediticio cuando llega un historial del ERP (consumidor,
/// endpoint interno o siembra de demo). Guarda historial, perfil, linea y, si el
/// resultado cambio, el evento PerfilCrediticioActualizado en el outbox, todo en un
/// solo SaveChanges.
/// </summary>
public sealed class ServicioPerfilCrediticio
{
    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly PoliticaCredito _politica;
    private readonly ILogger<ServicioPerfilCrediticio> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioPerfilCrediticio(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        PoliticaCredito politica,
        ILogger<ServicioPerfilCrediticio> logger)
        : this(repositorio, outbox, politica, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioPerfilCrediticio(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        PoliticaCredito politica,
        ILogger<ServicioPerfilCrediticio> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _politica = politica;
        _logger = logger;
        _reloj = reloj;
    }

    /// <summary>
    /// Aplica el historial si su fecha de corte es posterior a la ya aplicada; si no,
    /// no cambia nada y devuelve Aplicado=false con el perfil vigente.
    ///
    /// <paramref name="confirmar" /> en false deja los cambios pendientes en el
    /// contexto sin guardarlos: lo usa el consumidor, cuya clase base guarda todo
    /// junto con la marca de mensaje procesado en la misma transaccion.
    /// </summary>
    /// <exception cref="ArgumentException">El historial no trae cliente o trae valores negativos.</exception>
    public async Task<ResultadoAplicacionHistorial> AplicarHistorialAsync(
        HistorialClienteErpActualizadoData datos,
        CancellationToken cancelacion,
        bool confirmar = true)
    {
        ArgumentNullException.ThrowIfNull(datos);
        Validar(datos);

        var clienteId = NormalizarCliente(datos.ClienteId);
        var ahora = _reloj();
        var fechaCorte = FechasCredito.Normalizar(datos.FechaCorte);
        var clienteDesde = FechasCredito.Normalizar(datos.ClienteDesde);
        var recibidoEn = FechasCredito.Normalizar(ahora);

        var historial = await _repositorio.ObtenerHistorialAsync(clienteId, cancelacion);
        if (historial is not null && historial.FechaCorte >= fechaCorte)
        {
            _logger.LogInformation(
                "Historial de {ClienteId} ignorado: fecha de corte {FechaCorte} no es posterior a la aplicada {FechaCorteAplicada}.",
                clienteId, datos.FechaCorte, historial.FechaCorte);

            var vigente = await _repositorio.ObtenerPerfilAsync(clienteId, cancelacion);
            return vigente is null
                ? Resultado(clienteId, false, false, _politica.Evaluar(historial, ahora), historial.FechaCorte, ahora)
                : Resultado(vigente, aplicado: false, huboCambios: false);
        }

        var moneda = string.IsNullOrWhiteSpace(datos.Moneda) ? _politica.Opciones.Moneda : datos.Moneda.Trim().ToUpperInvariant();
        if (historial is null)
        {
            historial = new HistorialCliente(
                clienteId, datos.CodigoClienteErp?.Trim() ?? string.Empty, clienteDesde,
                datos.ComprasUltimos12Meses, datos.MontoCompradoUltimos12Meses, moneda,
                datos.PagosPuntuales, datos.PagosTardios, datos.MaxDiasAtraso, datos.Devoluciones,
                datos.IncidentesGraves, datos.OtrosIncidentes, fechaCorte, datos.LoteOrigen, recibidoEn);
            _repositorio.AgregarHistorial(historial);
        }
        else
        {
            historial.Actualizar(
                datos.CodigoClienteErp?.Trim() ?? string.Empty, clienteDesde,
                datos.ComprasUltimos12Meses, datos.MontoCompradoUltimos12Meses, moneda,
                datos.PagosPuntuales, datos.PagosTardios, datos.MaxDiasAtraso, datos.Devoluciones,
                datos.IncidentesGraves, datos.OtrosIncidentes, fechaCorte, datos.LoteOrigen, recibidoEn);
        }

        var (perfil, huboCambios) = await EvaluarYAplicarAsync(clienteId, historial, ahora, cancelacion);

        if (confirmar)
        {
            await _repositorio.GuardarCambiosAsync(cancelacion);
        }

        return Resultado(perfil, aplicado: true, huboCambios);
    }

    /// <summary>
    /// Recalcula el perfil con el historial vigente y el comportamiento propio actual
    /// (cuentas pagadas a tiempo, tarde y vencidas), dentro de la unidad de trabajo de
    /// quien llama: NO guarda. Lo usan la cobranza (pago total, vencimiento) y la
    /// anulacion. Sin historial no hay perfil que recalcular y devuelve false.
    /// </summary>
    public async Task<bool> RecalcularSinGuardarAsync(string clienteId, CancellationToken cancelacion)
    {
        var cliente = NormalizarCliente(clienteId);
        var historial = await _repositorio.ObtenerHistorialAsync(cliente, cancelacion);
        if (historial is null)
        {
            return false;
        }

        var (_, huboCambios) = await EvaluarYAplicarAsync(cliente, historial, _reloj(), cancelacion);
        return huboCambios;
    }

    /// <summary>
    /// Mora y perfil en un solo lugar, sin guardar: si el cliente tiene alguna cuenta
    /// VENCIDA con saldo la linea queda en pausa (BLOQUEADA); si no, se le quita la
    /// pausa. Despues recalcula el perfil con el comportamiento propio actualizado
    /// (DEUDA_VENCIDA mientras haya vencidas; pagos tardios o puntuales al pagar).
    /// </summary>
    public async Task ActualizarMoraYPerfilAsync(string clienteId, CancellationToken cancelacion)
    {
        var cliente = NormalizarCliente(clienteId);
        var ahora = _reloj();
        var cuentas = await _repositorio.ListarCuentasClienteAsync(cliente, cancelacion);
        var linea = await _repositorio.ObtenerLineaAsync(cliente, cancelacion);

        if (linea is not null)
        {
            if (cuentas.Any(c => c.Estado == EstadoCuentaPorCobrar.VENCIDA && c.SaldoPendiente > 0m))
            {
                linea.Bloquear(ahora);
            }
            else
            {
                linea.Desbloquear(ahora);
            }
        }

        await RecalcularSinGuardarAsync(cliente, cancelacion);
    }

    /// <summary>
    /// Comportamiento propio a partir de las cuentas del cliente. Usa las instancias
    /// con seguimiento, asi incluye los cambios aun no guardados de esta unidad de trabajo.
    /// </summary>
    public async Task<ComportamientoPropio> ComportamientoAsync(string clienteId, CancellationToken cancelacion)
    {
        var cuentas = await _repositorio.ListarCuentasClienteAsync(clienteId, cancelacion);
        return new ComportamientoPropio(
            cuentas.Count(c => c.Estado == EstadoCuentaPorCobrar.PAGADA && c.PagoPuntual == true),
            cuentas.Count(c => c.Estado == EstadoCuentaPorCobrar.PAGADA && c.PagoPuntual == false),
            cuentas.Count(c => c.Estado == EstadoCuentaPorCobrar.VENCIDA && c.SaldoPendiente > 0m));
    }

    private async Task<(PerfilCrediticio Perfil, bool HuboCambios)> EvaluarYAplicarAsync(
        string clienteId,
        HistorialCliente historial,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var comportamiento = await ComportamientoAsync(clienteId, cancelacion);
        var evaluacion = _politica.Evaluar(historial, comportamiento, ahora);

        var perfil = await _repositorio.ObtenerPerfilAsync(clienteId, cancelacion);
        bool huboCambios;
        if (perfil is null)
        {
            perfil = new PerfilCrediticio(clienteId, evaluacion, historial.FechaCorte, ahora);
            _repositorio.AgregarPerfil(perfil);
            huboCambios = true;
        }
        else
        {
            huboCambios = perfil.Recalcular(evaluacion, historial.FechaCorte, ahora);
        }

        var linea = await _repositorio.ObtenerLineaAsync(clienteId, cancelacion);
        if (linea is null)
        {
            linea = new LineaCredito(clienteId, _politica.Opciones.Moneda, ahora);
            _repositorio.AgregarLinea(linea);
        }

        linea.Recalcular(evaluacion.Elegible, evaluacion.LimiteAprobado, _politica.Opciones.Moneda, ahora);

        if (huboCambios)
        {
            _outbox.Encolar(
                PerfilCrediticioActualizado.EventType,
                PerfilCrediticioActualizado.AggregateType,
                clienteId,
                perfil.SiguienteSecuencia(),
                TopologiaMensajeria.ExchangeEventos,
                PerfilCrediticioActualizado.RoutingKey,
                new PerfilCrediticioActualizadoData(
                    clienteId,
                    perfil.Puntaje,
                    perfil.Nivel.ToString(),
                    perfil.Elegible,
                    perfil.CodigoMotivo,
                    perfil.LimiteAprobado,
                    _politica.Opciones.Moneda,
                    perfil.VersionPolitica,
                    perfil.FechaCorteHistorial,
                    perfil.CalculadoEn));
        }

        MetricasCredito.PerfilesRecalculadosTotal.Inc();
        _logger.LogInformation(
            "Perfil crediticio de {ClienteId} recalculado: nivel {Nivel}, elegible {Elegible}, motivo {Motivo}, cambios {HuboCambios}.",
            clienteId, perfil.Nivel, perfil.Elegible, perfil.CodigoMotivo ?? "ninguno", huboCambios);

        return (perfil, huboCambios);
    }

    public static string NormalizarCliente(string clienteId) => clienteId.Trim().ToLowerInvariant();

    private ResultadoAplicacionHistorial Resultado(PerfilCrediticio perfil, bool aplicado, bool huboCambios) =>
        new(
            perfil.ClienteId,
            aplicado,
            huboCambios,
            perfil.Puntaje,
            perfil.Nivel.ToString(),
            perfil.Elegible,
            perfil.CodigoMotivo,
            perfil.LimiteAprobado,
            _politica.Opciones.Moneda,
            perfil.VersionPolitica,
            perfil.FechaCorteHistorial,
            perfil.CalculadoEn);

    private ResultadoAplicacionHistorial Resultado(
        string clienteId, bool aplicado, bool huboCambios, EvaluacionCredito evaluacion, DateTime fechaCorte, DateTime calculadoEn) =>
        new(
            clienteId,
            aplicado,
            huboCambios,
            evaluacion.Puntaje,
            evaluacion.Nivel.ToString(),
            evaluacion.Elegible,
            evaluacion.CodigoMotivo,
            evaluacion.LimiteAprobado,
            _politica.Opciones.Moneda,
            evaluacion.VersionPolitica,
            fechaCorte,
            calculadoEn);

    private static void Validar(HistorialClienteErpActualizadoData datos)
    {
        if (string.IsNullOrWhiteSpace(datos.ClienteId))
        {
            throw new ArgumentException("El historial no identifica al cliente.", nameof(datos));
        }

        if (NormalizarCliente(datos.ClienteId).Length > HistorialCliente.LargoMaximoClienteId)
        {
            throw new ArgumentException("El identificador del cliente es demasiado largo.", nameof(datos));
        }

        if (datos.ComprasUltimos12Meses < 0 || datos.MontoCompradoUltimos12Meses < 0m
            || datos.PagosPuntuales < 0 || datos.PagosTardios < 0 || datos.MaxDiasAtraso < 0
            || datos.Devoluciones < 0 || datos.IncidentesGraves < 0 || datos.OtrosIncidentes < 0)
        {
            throw new ArgumentException("El historial trae valores negativos.", nameof(datos));
        }

        if (datos.FechaCorte == default || datos.ClienteDesde == default)
        {
            throw new ArgumentException("El historial debe traer fecha de corte y fecha de alta del cliente.", nameof(datos));
        }
    }
}
