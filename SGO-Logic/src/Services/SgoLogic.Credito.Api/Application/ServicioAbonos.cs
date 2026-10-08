using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>Codigos de error al crear o reintentar un abono; el endpoint los traduce a HTTP.</summary>
public static class ErroresAbono
{
    public const string CuentaNoEncontrada = "cuenta_no_encontrada";
    public const string AbonoEnCurso = "abono_en_curso";
    public const string CuentaNoPagable = "cuenta_no_pagable";
    public const string MontoInvalido = "monto_invalido";
    public const string TokenRequerido = "token_requerido";
    public const string AbonoNoEncontrado = "abono_no_encontrado";
    public const string AbonoNoReintentable = "abono_no_reintentable";
}

/// <summary>Resultado de una operacion de abono: el abono, o un codigo de error con su mensaje para el cliente.</summary>
public sealed record ResultadoAbono(AbonoDto? Abono, string? CodigoError = null, string? MensajeError = null)
{
    public bool Exito => Abono is not null && CodigoError is null;

    public static ResultadoAbono Error(string codigo, string mensaje) => new(null, codigo, mensaje);
}

/// <summary>
/// Pagos (abonos) del cliente a sus cuentas por cobrar. Credito solo registra el
/// abono y pide el cobro por outbox (CobroAbonoCreditoSolicitado); el cobro lo hace
/// Pagos y el resultado llega por la cola credito.pagos. Credito nunca llama a
/// PayKlik ni a Pagos por HTTP.
/// </summary>
public sealed class ServicioAbonos
{
    private readonly IRepositorioCredito _repositorio;
    private readonly IOutboxWriter _outbox;
    private readonly CobranzaOptions _opciones;
    private readonly ILogger<ServicioAbonos> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioAbonos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        IOptions<CobranzaOptions> opciones,
        ILogger<ServicioAbonos> logger)
        : this(repositorio, outbox, opciones, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioAbonos(
        IRepositorioCredito repositorio,
        IOutboxWriter outbox,
        IOptions<CobranzaOptions> opciones,
        ILogger<ServicioAbonos> logger,
        Func<DateTime> reloj)
    {
        _repositorio = repositorio;
        _outbox = outbox;
        _opciones = opciones.Value;
        _logger = logger;
        _reloj = reloj;
    }

    /// <summary>
    /// Crea el abono y pide el cobro en la misma transaccion. Reglas: la cuenta es del
    /// cliente y esta VIGENTE o VENCIDA con saldo; monto mayor que 0, no mayor que el
    /// saldo y al menos el minimo (si el saldo es menor que el minimo, solo el saldo
    /// completo); un solo abono en curso por cuenta.
    /// </summary>
    public async Task<ResultadoAbono> CrearAsync(
        string clienteId,
        string? cuentaId,
        decimal monto,
        string? tokenMedioPago,
        CancellationToken cancelacion)
    {
        var cliente = ServicioPerfilCrediticio.NormalizarCliente(clienteId);
        var cuenta = Guid.TryParse(cuentaId, out var id) ? await _repositorio.ObtenerCuentaAsync(id, cancelacion) : null;

        // Una cuenta de otro cliente se trata igual que una inexistente.
        if (cuenta is null || !string.Equals(cuenta.ClienteId, cliente, StringComparison.Ordinal))
        {
            return ResultadoAbono.Error(ErroresAbono.CuentaNoEncontrada, CatalogoMensajesCredito.CuentaNoEncontrada);
        }

        if (!cuenta.EsPagable)
        {
            return ResultadoAbono.Error(ErroresAbono.CuentaNoPagable, CatalogoMensajesCredito.CuentaNoPagable);
        }

        if (!MontoValido(monto, cuenta.SaldoPendiente))
        {
            return ResultadoAbono.Error(
                ErroresAbono.MontoInvalido,
                CatalogoMensajesCredito.MontoInvalido(cuenta.SaldoPendiente, _opciones.MontoMinimoAbono));
        }

        if (string.IsNullOrWhiteSpace(tokenMedioPago))
        {
            return ResultadoAbono.Error(ErroresAbono.TokenRequerido, "Valida tu tarjeta antes de pagar.");
        }

        if (await _repositorio.HayAbonoEnCursoAsync(cuenta.Id, cancelacion))
        {
            return ResultadoAbono.Error(ErroresAbono.AbonoEnCurso, CatalogoMensajesCredito.AbonoYaEnCurso);
        }

        var ahora = _reloj();
        var correlativo = await _repositorio.SiguienteCorrelativoAbonoAsync(cancelacion);
        var abono = new AbonoCredito($"ABO-{ahora:yyyy}-{correlativo:D6}", cuenta, monto, ahora);
        _repositorio.AgregarAbono(abono);
        EncolarCobro(abono, tokenMedioPago.Trim());

        try
        {
            await _repositorio.GuardarCambiosAsync(cancelacion);
        }
        catch (ExcepcionDuplicadoCredito)
        {
            // Otro abono para la misma cuenta se registro al mismo tiempo (indice unico).
            _repositorio.DescartarCambios();
            return ResultadoAbono.Error(ErroresAbono.AbonoEnCurso, CatalogoMensajesCredito.AbonoYaEnCurso);
        }

        MetricasCredito.AbonosTotal.WithLabels("solicitado").Inc();
        _logger.LogInformation(
            "Abono {Referencia} de {Monto} solicitado para la cuenta {CuentaId} de {ClienteId}.",
            abono.Referencia, monto, cuenta.Id, cliente);

        return new ResultadoAbono(Dto(abono, cuenta));
    }

    /// <summary>El abono, solo si es del cliente.</summary>
    public async Task<ResultadoAbono> ObtenerAsync(string clienteId, string? abonoId, CancellationToken cancelacion)
    {
        var (abono, cuenta) = await CargarAsync(clienteId, abonoId, cancelacion);
        return abono is null || cuenta is null
            ? ResultadoAbono.Error(ErroresAbono.AbonoNoEncontrado, CatalogoMensajesCredito.AbonoNoEncontrado)
            : new ResultadoAbono(Dto(abono, cuenta));
    }

    /// <summary>
    /// Reintento de un abono PENDIENTE: el MISMO abono (mismo Id) con el intento
    /// siguiente y el token nuevo. Pagos reutiliza el mismo pago y la misma clave de
    /// idempotencia hacia el proveedor, asi que no puede haber cobro doble.
    /// </summary>
    public async Task<ResultadoAbono> ReintentarAsync(
        string clienteId,
        string? abonoId,
        string? tokenMedioPago,
        CancellationToken cancelacion)
    {
        var (abono, cuenta) = await CargarAsync(clienteId, abonoId, cancelacion);
        if (abono is null || cuenta is null)
        {
            return ResultadoAbono.Error(ErroresAbono.AbonoNoEncontrado, CatalogoMensajesCredito.AbonoNoEncontrado);
        }

        if (!abono.PuedeReintentar)
        {
            return ResultadoAbono.Error(ErroresAbono.AbonoNoReintentable, CatalogoMensajesCredito.AbonoNoReintentable);
        }

        if (string.IsNullOrWhiteSpace(tokenMedioPago))
        {
            return ResultadoAbono.Error(ErroresAbono.TokenRequerido, "Valida tu tarjeta antes de pagar.");
        }

        abono.Reintentar(_reloj());
        EncolarCobro(abono, tokenMedioPago.Trim());
        await _repositorio.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation("Abono {Referencia} reintentado (intento {Intento}).", abono.Referencia, abono.IntentoNumero);
        return new ResultadoAbono(Dto(abono, cuenta));
    }

    public bool MontoValido(decimal monto, decimal saldo)
    {
        if (monto <= 0m || monto > saldo || decimal.Round(monto, 2) != monto)
        {
            return false;
        }

        return saldo < _opciones.MontoMinimoAbono ? monto == saldo : monto >= _opciones.MontoMinimoAbono;
    }

    /// <summary>Titulo y mensaje para el cliente segun el estado del abono.</summary>
    public static AbonoDto Dto(AbonoCredito abono, CuentaPorCobrar cuenta)
    {
        var mensaje = abono.Estado switch
        {
            EstadoAbonoCredito.SOLICITADO => CatalogoMensajesCredito.AbonoEnProceso(abono.Monto),
            EstadoAbonoCredito.PENDIENTE => CatalogoMensajesCredito.AbonoPendiente(),
            EstadoAbonoCredito.RECHAZADO => CatalogoMensajesCredito.AbonoRechazado(),
            EstadoAbonoCredito.REEMBOLSO_PENDIENTE => CatalogoMensajesCredito.AbonoReembolsoPendiente(),
            _ => cuenta.Estado == EstadoCuentaPorCobrar.PAGADA
                ? CatalogoMensajesCredito.AbonoAplicadoTotal(abono.Monto)
                : CatalogoMensajesCredito.AbonoAplicadoParcial(abono.Monto, cuenta.SaldoPendiente)
        };

        return new AbonoDto(
            abono.Id.ToString("N"),
            abono.Referencia,
            abono.CuentaId.ToString("N"),
            abono.Estado.ToString(),
            abono.EnCurso,
            mensaje.Titulo,
            mensaje.Mensaje,
            mensaje.Recomendacion,
            abono.Monto,
            abono.Moneda,
            cuenta.SaldoPendiente,
            abono.PuedeReintentar);
    }

    private async Task<(AbonoCredito? Abono, CuentaPorCobrar? Cuenta)> CargarAsync(
        string clienteId, string? abonoId, CancellationToken cancelacion)
    {
        var cliente = ServicioPerfilCrediticio.NormalizarCliente(clienteId);
        var abono = Guid.TryParse(abonoId, out var id) ? await _repositorio.ObtenerAbonoAsync(id, cancelacion) : null;
        if (abono is null || !string.Equals(abono.ClienteId, cliente, StringComparison.Ordinal))
        {
            return (null, null);
        }

        return (abono, await _repositorio.ObtenerCuentaAsync(abono.CuentaId, cancelacion));
    }

    private void EncolarCobro(AbonoCredito abono, string token) =>
        _outbox.Encolar(
            CobroAbonoCreditoSolicitado.EventType,
            CobroAbonoCreditoSolicitado.AggregateType,
            abono.Id.ToString("N"),
            abono.IntentoNumero,
            TopologiaMensajeria.ExchangeEventos,
            CobroAbonoCreditoSolicitado.RoutingKey,
            new CobroAbonoCreditoSolicitadoData(
                abono.Id.ToString("N"),
                abono.Referencia,
                abono.ClienteId,
                abono.Moneda,
                abono.Monto,
                token,
                abono.IntentoNumero,
                abono.SolicitadoEn));
}
