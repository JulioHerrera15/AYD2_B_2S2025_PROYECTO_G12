using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Nucleo de HU-004. Procesa un cobro contra PayKlik y encola el evento de
/// resultado. Lo usan el consumidor de <c>ordenes.cobro.solicitado</c> y el
/// endpoint de reintento.
///
/// No llama a <c>SaveChangesAsync</c>: la transaccion la cierra quien llama
/// (EventConsumerBase para el consumo, el controlador para el reintento), de modo
/// que el intento, el cambio de estado del pago, el enlace, la fila de outbox y
/// (en el consumo) la marca de mensaje procesado quedan en una sola transaccion.
///
/// Reintentos con backoff (ADR-009): alrededor de la unidad "registrar intento +
/// invocar proveedor". Cada llamada saliente produce su propia fila en
/// pagos.intento_pago, con <c>IntentoNumero</c> incremental dentro del mismo pago.
/// Se reintenta solo ante timeout, error de conexion o 5xx; nunca ante rechazo de
/// negocio ni con el circuito abierto.
///
/// Una falla del proveedor (timeout, 5xx, red) o el circuito abierto NO lanzan
/// excepcion: son un resultado de negocio valido y se traducen a pago PENDIENTE
/// con enlace de reintento. Solo suben las excepciones tecnicas del propio lado
/// (base caida), para que el consumidor nackee.
/// </summary>
public sealed class ServicioCobro
{
    private readonly PagosDbContext _contexto;
    private readonly IClientePayKlik _payklik;
    private readonly IOutboxWriter _outbox;
    private readonly PayKlikOptions _payklikOpciones;
    private readonly ReintentoOptions _reintento;
    private readonly ILogger<ServicioCobro> _logger;

    public ServicioCobro(
        PagosDbContext contexto,
        IClientePayKlik payklik,
        IOutboxWriter outbox,
        IOptions<PayKlikOptions> payklikOpciones,
        IOptions<ReintentoOptions> reintentoOpciones,
        ILogger<ServicioCobro> logger)
    {
        _contexto = contexto;
        _payklik = payklik;
        _outbox = outbox;
        _payklikOpciones = payklikOpciones.Value;
        _reintento = reintentoOpciones.Value;
        _logger = logger;
    }

    public async Task<Pago> ProcesarCobroAsync(SolicitudCobro solicitud, CancellationToken cancelacion)
    {
        var pago = await _contexto.Pagos
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == solicitud.OrdenId, cancelacion);

        if (pago is null)
        {
            pago = new Pago(
                solicitud.OrdenId,
                solicitud.ClienteId,
                solicitud.Canal,
                solicitud.Moneda,
                solicitud.Monto,
                solicitud.MedioPago);
            _contexto.Pagos.Add(pago);
        }

        // El header Idempotency-Key hacia PayKlik es el identificador del PAGO, no
        // del intento: asi los reintentos (automaticos o por enlace) no duplican el
        // cargo.
        var claveIdempotencia = pago.Id.ToString();
        var maxLlamadas = 1 + Math.Max(0, _payklikOpciones.MaxReintentos);

        for (var numeroLlamada = 1; numeroLlamada <= maxLlamadas; numeroLlamada++)
        {
            // Paso clave de HU-004: cada llamada saliente registra su propio intento
            // en INICIADO ANTES de invocar al proveedor, aunque este nunca responda
            // o el circuito este abierto.
            var intento = pago.RegistrarIntento(
                solicitud.MedioPago, solicitud.TokenMedioPago, solicitud.UltimosCuatro, solicitud.Marca);
            _contexto.Intentos.Add(intento);

            var peticion = new PeticionCargoPayKlik(
                pago.OrdenId,
                pago.Monto.ToString("0.00", CultureInfo.InvariantCulture),
                pago.Moneda,
                pago.MedioPago,
                solicitud.TokenMedioPago);

            var cronometro = Stopwatch.StartNew();
            ResultadoPayKlik? resultado = null;
            string codigoFalla = string.Empty;
            string motivoFalla = string.Empty;
            var circuitoAbierto = false;

            try
            {
                resultado = await _payklik.CargarAsync(peticion, claveIdempotencia, cancelacion);
            }
            catch (CircuitoAbiertoException)
            {
                (codigoFalla, motivoFalla) =
                    ("CIRCUITO_ABIERTO", "El disyuntor de PayKlik esta abierto; no se invoco al proveedor.");
                circuitoAbierto = true;
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

            cronometro.Stop();
            var latenciaMs = (int)cronometro.ElapsedMilliseconds;
            var ocurridoEn = DateTime.UtcNow;

            if (resultado is not null)
            {
                switch (resultado.Estado)
                {
                    case EstadoRespuestaPayKlik.APROBADO:
                        intento.MarcarExitoso(
                            resultado.AutorizacionId ?? string.Empty,
                            resultado.CodigoRespuesta,
                            resultado.Mensaje,
                            resultado.UltimosCuatro,
                            resultado.Marca,
                            latenciaMs,
                            ocurridoEn);
                        pago.MarcarAplicado(ocurridoEn);
                        EncolarPagoAplicado(pago, intento, resultado, ocurridoEn);
                        return pago;

                    case EstadoRespuestaPayKlik.RECHAZADO:
                        intento.MarcarRechazado(
                            resultado.CodigoRespuesta, resultado.Mensaje, latenciaMs, ocurridoEn);
                        pago.MarcarRechazado(ocurridoEn);
                        EncolarPagoRechazado(pago, intento, resultado, ocurridoEn);
                        return pago;
                }
            }

            // Sin respuesta util: fallo tecnico del proveedor o circuito abierto.
            intento.MarcarFallido(codigoFalla, motivoFalla, latenciaMs, ocurridoEn);

            var puedeReintentar = !circuitoAbierto && numeroLlamada < maxLlamadas;
            if (puedeReintentar)
            {
                var espera = CalcularBackoff(numeroLlamada, _payklikOpciones.BackoffBaseMs);
                _logger.LogWarning(
                    "Cobro de {OrdenId}: intento {Intento} fallo ({Codigo}); se reintenta en {EsperaMs} ms.",
                    pago.OrdenId, intento.IntentoNumero, codigoFalla, (int)espera.TotalMilliseconds);
                await Task.Delay(espera, cancelacion);
                continue;
            }

            _logger.LogWarning(
                "Cobro de {OrdenId} sin respuesta de PayKlik ({Codigo}); el pago queda PENDIENTE.",
                pago.OrdenId, codigoFalla);

            pago.MarcarPendiente(ocurridoEn);

            var enlace = pago.GenerarEnlaceReintento(_reintento.Expiracion, ocurridoEn);
            _contexto.EnlacesReintento.Add(enlace);

            EncolarPagoPendiente(pago, intento, codigoFalla, motivoFalla, enlace, ocurridoEn);
            return pago;
        }

        // Inalcanzable: el bucle siempre retorna dentro de alguna de sus ramas.
        throw new InvalidOperationException("El bucle de cobro no produjo un resultado.");
    }

    /// <summary>Backoff exponencial con jitter (jitter equitativo): mitad fija, mitad aleatoria.</summary>
    private static TimeSpan CalcularBackoff(int numeroLlamada, int backoffBaseMs)
    {
        if (backoffBaseMs <= 0)
        {
            return TimeSpan.Zero;
        }

        var tope = backoffBaseMs * Math.Pow(2, numeroLlamada - 1);
        var mitad = tope / 2;
        var jitter = Random.Shared.NextDouble() * mitad;
        return TimeSpan.FromMilliseconds(mitad + jitter);
    }

    private void EncolarPagoAplicado(Pago pago, IntentoPago intento, ResultadoPayKlik resultado, DateTime ocurridoEn) =>
        _outbox.Encolar(
            PagoAplicado.EventType,
            PagoAplicado.AggregateType,
            pago.OrdenId,
            pago.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            PagoAplicado.RoutingKey,
            new PagoAplicadoData(
                OrdenId: pago.OrdenId,
                ClienteId: pago.ClienteId,
                Canal: pago.Canal,
                Moneda: pago.Moneda,
                Monto: pago.Monto,
                MedioPago: pago.MedioPago,
                IntentoNumero: intento.IntentoNumero,
                Estado: EstadoPago.APLICADO,
                CodigoRespuesta: resultado.CodigoRespuesta,
                AutorizacionId: resultado.AutorizacionId ?? string.Empty,
                UltimosCuatro: intento.UltimosCuatro,
                Marca: intento.Marca,
                OcurridoEn: ocurridoEn));

    private void EncolarPagoRechazado(Pago pago, IntentoPago intento, ResultadoPayKlik resultado, DateTime ocurridoEn) =>
        _outbox.Encolar(
            PagoRechazado.EventType,
            PagoRechazado.AggregateType,
            pago.OrdenId,
            pago.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            PagoRechazado.RoutingKey,
            new PagoRechazadoData(
                OrdenId: pago.OrdenId,
                ClienteId: pago.ClienteId,
                Canal: pago.Canal,
                Moneda: pago.Moneda,
                Monto: pago.Monto,
                MedioPago: pago.MedioPago,
                IntentoNumero: intento.IntentoNumero,
                Estado: EstadoPago.RECHAZADO,
                CodigoRespuesta: resultado.CodigoRespuesta,
                OcurridoEn: ocurridoEn));

    private void EncolarPagoPendiente(
        Pago pago,
        IntentoPago intento,
        string codigoRespuesta,
        string motivoFalla,
        EnlaceReintento enlace,
        DateTime ocurridoEn) =>
        _outbox.Encolar(
            PagoPendiente.EventType,
            PagoPendiente.AggregateType,
            pago.OrdenId,
            pago.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            PagoPendiente.RoutingKey,
            new PagoPendienteData(
                OrdenId: pago.OrdenId,
                ClienteId: pago.ClienteId,
                Canal: pago.Canal,
                Moneda: pago.Moneda,
                Monto: pago.Monto,
                MedioPago: pago.MedioPago,
                IntentoNumero: intento.IntentoNumero,
                Estado: EstadoPago.PENDIENTE,
                CodigoRespuesta: codigoRespuesta,
                MotivoFalla: motivoFalla,
                EnlaceReintento: new EnlaceReintentoData(
                    enlace.Token, $"/api/pagos/reintentos/{enlace.Token}", enlace.ExpiraEn),
                OcurridoEn: ocurridoEn));
}
