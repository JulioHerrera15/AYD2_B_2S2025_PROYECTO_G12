using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Application.Pagos;

public enum ResultadoCreacionReembolso
{
    Creado,

    /// <summary>Ya existia un reembolso para esa referencia: no se crea otro.</summary>
    Duplicado,

    /// <summary>El evento no pide reembolso (sin RequiereReembolso, a credito o sin pago aplicado).</summary>
    Ignorado,

    /// <summary>No hay cobro que devolver o el monto ya no cabe: se emitio el evento de fallo.</summary>
    Rechazado
}

/// <summary>
/// Reembolsos que no se pierden: por cancelacion de una orden pagada con tarjeta
/// (OrdenCancelada) y de abonos a credito (ReembolsoAbonoCreditoSolicitado).
///
/// La creacion registra el reembolso PENDIENTE y no llama a SaveChanges: lo guarda el
/// consumidor junto con la marca de mensaje procesado. El envio al proveedor lo hace
/// <see cref="ProcesarPendientesAsync" /> (lo invoca ReembolsosPendientesWorker), con
/// Idempotency-Key = Id del reembolso en todos los intentos:
/// <list type="bullet">
///   <item>Aprobado: APLICADO y evento de exito.</item>
///   <item>Rechazo del proveedor: FALLIDO y evento de fallo, sin reintentar.</item>
///   <item>Falla tecnica (timeout, 5xx, red, circuito abierto): se reprograma con
///   backoff; al agotar Pagos:Reembolsos:MaxIntentos queda FALLIDO con evento de fallo.</item>
/// </list>
/// Cada reembolso se resuelve y se guarda por separado, con su evento en el outbox en
/// la misma transaccion. A diferencia de ServicioReembolso, no registra intentos en el
/// pago original: el pago sigue reflejando solo su cobro.
/// </summary>
public sealed class ServicioReembolsosPendientes
{
    private readonly PagosDbContext _contexto;
    private readonly IClientePayKlik _payklik;
    private readonly IOutboxWriter _outbox;
    private readonly ReembolsosOptions _opciones;
    private readonly ILogger<ServicioReembolsosPendientes> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioReembolsosPendientes(
        PagosDbContext contexto,
        IClientePayKlik payklik,
        IOutboxWriter outbox,
        IOptions<ReembolsosOptions> opciones,
        ILogger<ServicioReembolsosPendientes> logger)
        : this(contexto, payklik, outbox, opciones, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioReembolsosPendientes(
        PagosDbContext contexto,
        IClientePayKlik payklik,
        IOutboxWriter outbox,
        IOptions<ReembolsosOptions> opciones,
        ILogger<ServicioReembolsosPendientes> logger,
        Func<DateTime> reloj)
    {
        _contexto = contexto;
        _payklik = payklik;
        _outbox = outbox;
        _opciones = opciones.Value;
        _logger = logger;
        _reloj = reloj;
    }

    /// <summary>
    /// OrdenCancelada: solo con RequiereReembolso, medio de pago distinto de CREDITO y un
    /// Pago APLICADO de concepto ORDEN. El monto es lo cobrado. Idempotente por ordenId.
    /// </summary>
    public async Task<ResultadoCreacionReembolso> CrearPorCancelacionAsync(OrdenCanceladaData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        if (!datos.RequiereReembolso || datos.MedioPago == MedioPago.CREDITO)
        {
            return ResultadoCreacionReembolso.Ignorado;
        }

        var pago = await _contexto.Pagos.FirstOrDefaultAsync(
            p => p.OrdenId == datos.OrdenId && p.Concepto == ConceptoPago.ORDEN && p.Estado == EstadoPago.APLICADO,
            cancelacion);
        if (pago is null)
        {
            _logger.LogWarning(
                "La orden {OrdenId} se cancelo pidiendo reembolso, pero no tiene un pago aplicado; no hay nada que devolver.",
                datos.OrdenId);
            return ResultadoCreacionReembolso.Ignorado;
        }

        if (await ExisteAsync(ConceptoReembolso.CANCELACION_ORDEN, datos.OrdenId, cancelacion))
        {
            return ResultadoCreacionReembolso.Duplicado;
        }

        var reembolso = new Reembolso(
            ConceptoReembolso.CANCELACION_ORDEN, datos.OrdenId, pago.Id, pago.ClienteId, pago.Monto, pago.Moneda, _reloj());
        _contexto.Reembolsos.Add(reembolso);

        MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(reembolso.Concepto), "pendiente").Inc();
        _logger.LogInformation(
            "Reembolso {ReembolsoId} PENDIENTE por la cancelacion de la orden {OrdenId}: {Monto} {Moneda}.",
            reembolso.Id, datos.OrdenId, reembolso.Monto, reembolso.Moneda);
        return ResultadoCreacionReembolso.Creado;
    }

    /// <summary>
    /// ReembolsoAbonoCreditoSolicitado: contra el Pago APLICADO de ese abono, idempotente
    /// por reembolsoId y sin superar lo cobrado menos lo ya reembolsado (o en curso). Si no
    /// hay cobro o ya no queda nada por devolver, emite ReembolsoAbonoCreditoFallido.
    /// </summary>
    public async Task<ResultadoCreacionReembolso> CrearPorAbonoAsync(
        ReembolsoAbonoCreditoSolicitadoData datos,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        if (await ExisteAsync(ConceptoReembolso.ABONO_CREDITO, datos.ReembolsoId, cancelacion))
        {
            return ResultadoCreacionReembolso.Duplicado;
        }

        var ahora = _reloj();
        var pago = await _contexto.Pagos.FirstOrDefaultAsync(
            p => p.OrdenId == datos.Referencia && p.Concepto == ConceptoPago.ABONO_CREDITO && p.Estado == EstadoPago.APLICADO,
            cancelacion);
        if (pago is null)
        {
            _logger.LogWarning(
                "Reembolso {ReembolsoId} del abono {Referencia} sin cobro aplicado; se informa el fallo.",
                datos.ReembolsoId, datos.Referencia);
            EncolarAbonoFallido(datos.ReembolsoId, datos.AbonoId, datos.ClienteId, datos.Moneda, datos.Monto,
                "No hay un cobro aplicado para este abono.", ahora);
            MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(ConceptoReembolso.ABONO_CREDITO), "fallido").Inc();
            return ResultadoCreacionReembolso.Rechazado;
        }

        var comprometido = await _contexto.Reembolsos
            .Where(r => r.PagoId == pago.Id && r.Concepto == ConceptoReembolso.ABONO_CREDITO && r.Estado != EstadoReembolso.FALLIDO)
            .Select(r => r.Monto)
            .ToListAsync(cancelacion);
        var disponible = pago.Monto - comprometido.Sum();
        var monto = Math.Min(datos.Monto, disponible);

        if (monto <= 0m)
        {
            _logger.LogWarning(
                "Reembolso {ReembolsoId} del abono {Referencia} por {Monto}: ya no queda monto cobrado por devolver.",
                datos.ReembolsoId, datos.Referencia, datos.Monto);
            EncolarAbonoFallido(datos.ReembolsoId, datos.AbonoId, datos.ClienteId, datos.Moneda, datos.Monto,
                "El monto supera lo cobrado en el abono.", ahora);
            MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(ConceptoReembolso.ABONO_CREDITO), "fallido").Inc();
            return ResultadoCreacionReembolso.Rechazado;
        }

        if (monto < datos.Monto)
        {
            _logger.LogWarning(
                "Reembolso {ReembolsoId} del abono {Referencia}: se piden {Pedido} pero solo quedan {Disponible} por devolver.",
                datos.ReembolsoId, datos.Referencia, datos.Monto, disponible);
        }

        var reembolso = new Reembolso(
            ConceptoReembolso.ABONO_CREDITO, datos.ReembolsoId, pago.Id, pago.ClienteId, monto, pago.Moneda, ahora, datos.AbonoId);
        _contexto.Reembolsos.Add(reembolso);

        MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(reembolso.Concepto), "pendiente").Inc();
        _logger.LogInformation(
            "Reembolso {ReembolsoId} PENDIENTE del abono {Referencia} ({Motivo}): {Monto} {Moneda}.",
            reembolso.Id, datos.Referencia, datos.Motivo, monto, reembolso.Moneda);
        return ResultadoCreacionReembolso.Creado;
    }

    /// <summary>
    /// Procesa los reembolsos PENDIENTE cuyo ProximoIntentoEn ya llego, uno por uno y
    /// guardando cada uno. Devuelve cuantos proceso. Un choque de concurrencia (otro
    /// worker lo tomo) se descarta y sigue con el siguiente.
    /// </summary>
    public async Task<int> ProcesarPendientesAsync(CancellationToken cancelacion)
    {
        var ahora = _reloj();
        var ids = await _contexto.Reembolsos.AsNoTracking()
            .Where(r => r.Estado == EstadoReembolso.PENDIENTE && r.ProximoIntentoEn != null && r.ProximoIntentoEn <= ahora)
            .OrderBy(r => r.ProximoIntentoEn)
            .Select(r => r.Id)
            .Take(Math.Max(1, _opciones.Lote))
            .ToListAsync(cancelacion);

        var procesados = 0;
        foreach (var id in ids)
        {
            try
            {
                if (await ProcesarUnoAsync(id, cancelacion))
                {
                    await _contexto.SaveChangesAsync(cancelacion);
                    procesados++;
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogInformation(ex, "El reembolso {ReembolsoId} lo resolvio otra instancia; se omite.", id);
            }
            finally
            {
                _contexto.ChangeTracker.Clear();
            }
        }

        return procesados;
    }

    private async Task<bool> ProcesarUnoAsync(Guid id, CancellationToken cancelacion)
    {
        var reembolso = await _contexto.Reembolsos.FirstOrDefaultAsync(r => r.Id == id, cancelacion);
        if (reembolso is null || reembolso.EsFinal)
        {
            return false;
        }

        var pago = await _contexto.Pagos.Include(p => p.Intentos).FirstOrDefaultAsync(p => p.Id == reembolso.PagoId, cancelacion);
        var ahora = _reloj();
        if (pago is null)
        {
            reembolso.MarcarFallido("SIN_PAGO", "El pago original ya no existe.", ahora, contarIntento: false);
            EncolarFallido(reembolso, reembolso.MotivoFalla!, ahora);
            return true;
        }

        var autorizacionOriginal = pago.Intentos
            .Where(i => i.Estado == EstadoIntentoPago.EXITOSO)
            .Select(i => i.AutorizacionId)
            .LastOrDefault() ?? string.Empty;

        var peticion = new PeticionReembolsoPayKlik(
            pago.OrdenId,
            reembolso.Monto.ToString("0.00", CultureInfo.InvariantCulture),
            reembolso.Moneda,
            autorizacionOriginal);

        ResultadoPayKlik? resultado = null;
        var codigoFalla = string.Empty;
        var motivoFalla = string.Empty;

        try
        {
            resultado = await _payklik.ReembolsoAsync(peticion, reembolso.Id.ToString(), cancelacion);
        }
        catch (CircuitoAbiertoException)
        {
            (codigoFalla, motivoFalla) = ("CIRCUITO_ABIERTO", "El disyuntor de PayKlik esta abierto; no se invoco al proveedor.");
        }
        catch (PayKlikNoDisponibleException ex)
        {
            (codigoFalla, motivoFalla) = ("PROVEEDOR_5XX", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            (codigoFalla, motivoFalla) = ("RED", $"Error de red invocando a PayKlik: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
        {
            (codigoFalla, motivoFalla) = ("TIMEOUT", "PayKlik no respondio dentro del timeout configurado");
        }

        ahora = _reloj();

        if (resultado?.Estado == EstadoRespuestaPayKlik.APROBADO)
        {
            reembolso.MarcarAplicado(resultado.AutorizacionId ?? string.Empty, resultado.CodigoRespuesta, ahora);
            EncolarAplicado(reembolso, ahora);
            MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(reembolso.Concepto), "aplicado").Inc();
            _logger.LogInformation(
                "Reembolso {ReembolsoId} ({Concepto} {Referencia}) aplicado en el intento {Intento}.",
                reembolso.Id, reembolso.Concepto, reembolso.ReferenciaId, reembolso.Intentos);
            return true;
        }

        if (resultado?.Estado == EstadoRespuestaPayKlik.RECHAZADO)
        {
            reembolso.MarcarFallido(resultado.CodigoRespuesta, resultado.Mensaje, ahora);
            EncolarFallido(reembolso, resultado.Mensaje, ahora);
            MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(reembolso.Concepto), "fallido").Inc();
            _logger.LogWarning(
                "Reembolso {ReembolsoId} ({Concepto} {Referencia}) rechazado por el proveedor ({Codigo}); no se reintenta.",
                reembolso.Id, reembolso.Concepto, reembolso.ReferenciaId, resultado.CodigoRespuesta);
            return true;
        }

        var agotado = reembolso.RegistrarFallaTecnica(
            codigoFalla,
            motivoFalla,
            ahora,
            Math.Max(1, _opciones.MaxIntentos),
            TimeSpan.FromSeconds(Math.Max(0, _opciones.BackoffBaseSegundos)),
            TimeSpan.FromSeconds(Math.Max(0, _opciones.BackoffMaximoSegundos)));

        if (agotado)
        {
            EncolarFallido(reembolso, $"Reintentos agotados ({codigoFalla}): {motivoFalla}", ahora);
            MetricasPagos.ReembolsosTotal.WithLabels(Etiqueta(reembolso.Concepto), "fallido").Inc();
            _logger.LogError(
                "Reembolso {ReembolsoId} ({Concepto} {Referencia}) agoto {Intentos} intentos ({Codigo}); queda FALLIDO.",
                reembolso.Id, reembolso.Concepto, reembolso.ReferenciaId, reembolso.Intentos, codigoFalla);
        }
        else
        {
            _logger.LogWarning(
                "Reembolso {ReembolsoId} ({Concepto} {Referencia}) sin respuesta util ({Codigo}); intento {Intento}, siguiente en {Proximo:o}.",
                reembolso.Id, reembolso.Concepto, reembolso.ReferenciaId, codigoFalla, reembolso.Intentos, reembolso.ProximoIntentoEn);
        }

        return true;
    }

    private Task<bool> ExisteAsync(ConceptoReembolso concepto, string referenciaId, CancellationToken cancelacion) =>
        _contexto.Reembolsos.AnyAsync(r => r.Concepto == concepto && r.ReferenciaId == referenciaId, cancelacion);

    private void EncolarAplicado(Reembolso reembolso, DateTime ahora)
    {
        if (reembolso.Concepto == ConceptoReembolso.CANCELACION_ORDEN)
        {
            _outbox.Encolar(
                ReembolsoCancelacionAplicado.EventType,
                ReembolsoCancelacionAplicado.AggregateType,
                reembolso.ReferenciaId,
                sequence: 1,
                TopologiaMensajeria.ExchangeEventos,
                ReembolsoCancelacionAplicado.RoutingKey,
                new ReembolsoCancelacionAplicadoData(
                    reembolso.ReferenciaId, reembolso.ClienteId, reembolso.Moneda, reembolso.Monto,
                    reembolso.AutorizacionId ?? string.Empty, ahora));
            return;
        }

        _outbox.Encolar(
            AbonoCreditoReembolsado.EventType,
            AbonoCreditoReembolsado.AggregateType,
            reembolso.ReferenciaId,
            sequence: 1,
            TopologiaMensajeria.ExchangeEventos,
            AbonoCreditoReembolsado.RoutingKey,
            new AbonoCreditoReembolsadoData(
                reembolso.ReferenciaId, reembolso.AbonoId ?? string.Empty, reembolso.ClienteId, reembolso.Moneda.ToString(),
                reembolso.Monto, reembolso.AutorizacionId ?? string.Empty, ahora));
    }

    private void EncolarFallido(Reembolso reembolso, string motivo, DateTime ahora)
    {
        if (reembolso.Concepto == ConceptoReembolso.CANCELACION_ORDEN)
        {
            _outbox.Encolar(
                ReembolsoCancelacionFallido.EventType,
                ReembolsoCancelacionFallido.AggregateType,
                reembolso.ReferenciaId,
                sequence: 1,
                TopologiaMensajeria.ExchangeEventos,
                ReembolsoCancelacionFallido.RoutingKey,
                new ReembolsoCancelacionFallidoData(
                    reembolso.ReferenciaId, reembolso.ClienteId, reembolso.Moneda, reembolso.Monto, motivo, ahora));
            return;
        }

        EncolarAbonoFallido(
            reembolso.ReferenciaId, reembolso.AbonoId ?? string.Empty, reembolso.ClienteId, reembolso.Moneda.ToString(),
            reembolso.Monto, motivo, ahora);
    }

    private void EncolarAbonoFallido(
        string reembolsoId, string abonoId, string clienteId, string moneda, decimal monto, string motivo, DateTime ahora) =>
        _outbox.Encolar(
            ReembolsoAbonoCreditoFallido.EventType,
            ReembolsoAbonoCreditoFallido.AggregateType,
            reembolsoId,
            sequence: 1,
            TopologiaMensajeria.ExchangeEventos,
            ReembolsoAbonoCreditoFallido.RoutingKey,
            new ReembolsoAbonoCreditoFallidoData(reembolsoId, abonoId, clienteId, moneda, monto, motivo, ahora));

    private static string Etiqueta(ConceptoReembolso concepto) => concepto switch
    {
        ConceptoReembolso.CANCELACION_ORDEN => "cancelacion_orden",
        _ => "abono_credito"
    };
}
