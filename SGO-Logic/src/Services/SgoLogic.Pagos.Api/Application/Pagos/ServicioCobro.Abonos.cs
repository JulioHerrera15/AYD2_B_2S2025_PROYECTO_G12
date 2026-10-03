using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Cobro de abonos a credito (CobroAbonoCreditoSolicitado). Reutiliza lo mismo que el
/// cobro de ordenes: cliente de PayKlik con disyuntor, boveda de tokens (el token debe
/// ser del cliente del abono) y registro de intentos. Diferencias:
/// <list type="bullet">
///   <item>Un Pago por abono con Concepto ABONO_CREDITO; Pago.OrdenId lleva la
///   referencia del abono. Las consultas de pagos por orden no lo ven.</item>
///   <item>La clave de idempotencia hacia PayKlik es el Id de ese Pago: un reintento
///   del mismo abono usa el mismo Pago y la misma clave, nunca un segundo cargo. Si el
///   Pago ya quedo APLICADO o RECHAZADO, se reemite el resultado sin llamar al proveedor.</item>
///   <item>Emite AbonoCreditoCobrado, AbonoCreditoRechazado o AbonoCreditoPendiente;
///   nunca PagoAplicado, PagoRechazado, PagoPendiente ni IntentoPagoFallido, y no genera
///   enlace de reintento (el reintento lo pide el cliente desde Mi credito).</item>
/// </list>
/// </summary>
public sealed partial class ServicioCobro
{
    public async Task<Pago> ProcesarCobroAbonoAsync(CobroAbonoCreditoSolicitadoData datos, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var moneda = Enum.TryParse<Moneda>(datos.Moneda, ignoreCase: true, out var valor) ? valor : Moneda.GTQ;
        var pago = await _contexto.Pagos
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == datos.Referencia && p.Concepto == ConceptoPago.ABONO_CREDITO, cancelacion);

        if (pago is null)
        {
            pago = new Pago(datos.Referencia, datos.ClienteId, Canal.WEB, moneda, datos.Monto, MedioPago.TARJETA,
                concepto: ConceptoPago.ABONO_CREDITO);
            _contexto.Pagos.Add(pago);
        }
        else if (pago.Estado is EstadoPago.APLICADO or EstadoPago.RECHAZADO)
        {
            // Ya hubo resultado definitivo para este abono: se reemite tal cual. Asi un
            // reintento tardio nunca vuelve a llegar al proveedor.
            ReemitirResultadoAbono(pago, datos);
            return pago;
        }

        if (string.IsNullOrWhiteSpace(datos.TokenMedioPago))
        {
            return RechazarAbonoSinInvocar(pago, datos, null, CodigosToken.Requerido, "El abono no trae token de medio de pago.");
        }

        var resolucion = await ResolverTokenAsync(datos.TokenMedioPago, pago, cancelacion);
        if (resolucion.CodigoRechazo is not null)
        {
            _logger.LogWarning(
                "Abono {Referencia} rechazado sin invocar a PayKlik: token no utilizable ({Codigo}).",
                datos.Referencia, resolucion.CodigoRechazo);
            return RechazarAbonoSinInvocar(pago, datos, resolucion.Token, resolucion.CodigoRechazo, resolucion.Mensaje!);
        }

        var medio = resolucion.Medio!;
        var claveIdempotencia = pago.Id.ToString();
        var maxLlamadas = 1 + Math.Max(0, _payklikOpciones.MaxReintentos);

        for (var numeroLlamada = 1; numeroLlamada <= maxLlamadas; numeroLlamada++)
        {
            var intento = pago.RegistrarIntento(MedioPago.TARJETA, datos.TokenMedioPago, medio.Token.Ultimos4, medio.Token.Marca.ToString());
            _contexto.Intentos.Add(intento);

            var peticion = new PeticionCargoPayKlik(
                pago.OrdenId,
                pago.Monto.ToString("0.00", CultureInfo.InvariantCulture),
                pago.Moneda,
                pago.MedioPago,
                Tarjeta: medio.Tarjeta,
                Guardar: medio.Token.GuardarAlCobrar ? true : null,
                ReferenciaTarjeta: medio.TarjetaGuardada?.ReferenciaPayKlik,
                Cvv: medio.TarjetaGuardada is not null ? medio.Cvv : null);

            var cronometro = Stopwatch.StartNew();
            ResultadoPayKlik? resultado = null;
            string codigoFalla;
            string motivoFalla;
            var circuitoAbierto = false;

            try
            {
                resultado = await _payklik.CargarAsync(peticion, claveIdempotencia, cancelacion);
                (codigoFalla, motivoFalla) = (string.Empty, string.Empty);
            }
            catch (CircuitoAbiertoException)
            {
                (codigoFalla, motivoFalla) = ("CIRCUITO_ABIERTO", "El disyuntor de PayKlik esta abierto; no se invoco al proveedor.");
                circuitoAbierto = true;
            }
            catch (TokenMedioPagoInvalidoException ex)
            {
                cronometro.Stop();
                var ahoraRechazo = DateTime.UtcNow;
                medio.Token.Consumir();
                intento.MarcarRechazado(ex.Codigo, ex.Message, (int)cronometro.ElapsedMilliseconds, ahoraRechazo);
                pago.MarcarRechazado(ahoraRechazo);
                EncolarAbonoRechazado(pago, datos, ex.Codigo, ahoraRechazo);
                return pago;
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

            if (resultado?.Estado == EstadoRespuestaPayKlik.APROBADO)
            {
                intento.MarcarExitoso(
                    resultado.AutorizacionId ?? string.Empty, resultado.CodigoRespuesta, resultado.Mensaje,
                    resultado.UltimosCuatro, resultado.Marca, latenciaMs, ocurridoEn);
                pago.MarcarAplicado(ocurridoEn);
                await CerrarTokenCobradoAsync(medio, resultado, ocurridoEn, cancelacion);
                EncolarAbonoCobrado(pago, datos, intento, ocurridoEn);
                _logger.LogInformation("Abono {Referencia} cobrado (autorizacion {Autorizacion}).", datos.Referencia, intento.AutorizacionId);
                return pago;
            }

            if (resultado?.Estado == EstadoRespuestaPayKlik.RECHAZADO)
            {
                intento.MarcarRechazado(
                    resultado.CodigoRespuesta, resultado.Mensaje, latenciaMs, ocurridoEn, resultado.UltimosCuatro, resultado.Marca);
                pago.MarcarRechazado(ocurridoEn);
                medio.Token.Consumir();
                EncolarAbonoRechazado(pago, datos, resultado.CodigoRespuesta, ocurridoEn);
                return pago;
            }

            // Sin respuesta util: el token sigue ACTIVO y el abono se puede reintentar.
            intento.MarcarFallido(codigoFalla, motivoFalla, latenciaMs, ocurridoEn);
            if (!circuitoAbierto && numeroLlamada < maxLlamadas)
            {
                await Task.Delay(CalcularBackoff(numeroLlamada, _payklikOpciones.BackoffBaseMs), cancelacion);
                continue;
            }

            _logger.LogWarning(
                "Abono {Referencia} sin respuesta de PayKlik ({Codigo}); queda PENDIENTE.", datos.Referencia, codigoFalla);
            pago.MarcarPendiente(ocurridoEn);
            EncolarAbonoPendiente(pago, datos, $"{codigoFalla}: {motivoFalla}", ocurridoEn);
            return pago;
        }

        throw new InvalidOperationException("El bucle de cobro del abono no produjo un resultado.");
    }

    private Pago RechazarAbonoSinInvocar(Pago pago, CobroAbonoCreditoSolicitadoData datos, TokenPago? token, string codigo, string mensaje)
    {
        var ahora = DateTime.UtcNow;
        var intento = pago.RegistrarIntento(MedioPago.TARJETA, datos.TokenMedioPago, token?.Ultimos4, token?.Marca.ToString());
        _contexto.Intentos.Add(intento);
        intento.MarcarRechazado(codigo, mensaje, latenciaMs: 0, ahora);
        pago.MarcarRechazado(ahora);
        EncolarAbonoRechazado(pago, datos, codigo, ahora);
        return pago;
    }

    private void ReemitirResultadoAbono(Pago pago, CobroAbonoCreditoSolicitadoData datos)
    {
        var ahora = DateTime.UtcNow;
        if (pago.Estado == EstadoPago.APLICADO)
        {
            var exitoso = pago.Intentos.LastOrDefault(i => i.Estado == EstadoIntentoPago.EXITOSO);
            EncolarAbonoCobrado(pago, datos, exitoso, ahora);
            _logger.LogInformation("Abono {Referencia} ya estaba cobrado; se reemite el resultado sin llamar a PayKlik.", datos.Referencia);
            return;
        }

        var rechazado = pago.Intentos.LastOrDefault(i => i.Estado == EstadoIntentoPago.RECHAZADO);
        EncolarAbonoRechazado(pago, datos, rechazado?.CodigoRespuesta ?? "RECHAZADO", ahora);
    }

    private void EncolarAbonoCobrado(Pago pago, CobroAbonoCreditoSolicitadoData datos, IntentoPago? intento, DateTime ocurridoEn) =>
        _outbox.Encolar(
            AbonoCreditoCobrado.EventType,
            AbonoCreditoCobrado.AggregateType,
            datos.AbonoId,
            pago.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            AbonoCreditoCobrado.RoutingKey,
            new AbonoCreditoCobradoData(
                datos.AbonoId,
                datos.Referencia,
                pago.ClienteId,
                pago.Moneda.ToString(),
                pago.Monto,
                intento?.AutorizacionId ?? string.Empty,
                intento?.UltimosCuatro,
                intento?.Marca,
                ocurridoEn));

    private void EncolarAbonoRechazado(Pago pago, CobroAbonoCreditoSolicitadoData datos, string codigo, DateTime ocurridoEn) =>
        _outbox.Encolar(
            AbonoCreditoRechazado.EventType,
            AbonoCreditoRechazado.AggregateType,
            datos.AbonoId,
            pago.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            AbonoCreditoRechazado.RoutingKey,
            new AbonoCreditoRechazadoData(datos.AbonoId, datos.Referencia, pago.ClienteId, codigo, ocurridoEn));

    private void EncolarAbonoPendiente(Pago pago, CobroAbonoCreditoSolicitadoData datos, string motivo, DateTime ocurridoEn) =>
        _outbox.Encolar(
            AbonoCreditoPendiente.EventType,
            AbonoCreditoPendiente.AggregateType,
            datos.AbonoId,
            pago.Secuencia,
            TopologiaMensajeria.ExchangeEventos,
            AbonoCreditoPendiente.RoutingKey,
            new AbonoCreditoPendienteData(datos.AbonoId, datos.Referencia, pago.ClienteId, motivo, ocurridoEn));
}
