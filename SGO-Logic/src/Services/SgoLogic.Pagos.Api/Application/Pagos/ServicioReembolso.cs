using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Procesa reembolsos contra PayKlik al consumir DevolucionRegistrada. Registra
/// el intento ANTES de invocar al proveedor (mismo patron que ServicioCobro),
/// encola ReembolsoAplicado o ReembolsoFallido via outbox segun la respuesta.
///
/// No llama a SaveChangesAsync: la transaccion la cierra el consumidor
/// (PagosEventosConsumidor), de modo que el intento, la fila de outbox y la
/// marca de mensaje procesado quedan en una sola transaccion.
///
/// Reintentos con backoff (ADR-009): misma logica que ServicioCobro. Cada llamada
/// saliente produce su propia fila en pagos.intento_pago, con IntentoNumero
/// incremental dentro del mismo pago.
///
/// Una falla del proveedor o el circuito abierto NO lanzan excepcion: son un
/// resultado de negocio valido y se traducen a ReembolsoFallido. Solo suben las
/// excepciones tecnicas del propio lado (base caida), para que el consumidor nackee.
/// </summary>
public sealed class ServicioReembolso
{
    private readonly PagosDbContext _contexto;
    private readonly IClientePayKlik _payklik;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<ServicioReembolso> _logger;

    public ServicioReembolso(
        PagosDbContext contexto,
        IClientePayKlik payklik,
        IOutboxWriter outbox,
        ILogger<ServicioReembolso> logger)
    {
        _contexto = contexto;
        _payklik = payklik;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task ProcesarReembolsoAsync(DevolucionRegistradaData datos, CancellationToken cancelacion)
    {
        var pago = await _contexto.Pagos
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == datos.OrdenOriginalId, cancelacion);

        if (pago is null)
        {
            pago = new Pago(
                datos.OrdenOriginalId,
                datos.ClienteId,
                Canal.WEB,
                Enum.Parse<Moneda>(datos.Moneda),
                decimal.Parse(datos.MontoReembolso, CultureInfo.InvariantCulture),
                MedioPago.TRANSFERENCIA);
            _contexto.Pagos.Add(pago);
        }

        var claveIdempotencia = datos.DevolucionId;

        var intento = pago.RegistrarIntento(
            MedioPago.TRANSFERENCIA, tokenMedioPago: null, ultimosCuatro: null, marca: null);
        _contexto.Intentos.Add(intento);

        var peticion = new PeticionReembolsoPayKlik(
            datos.DevolucionId,
            datos.MontoReembolso,
            Enum.Parse<Moneda>(datos.Moneda),
            datos.PagoOriginalId);

        var cronometro = Stopwatch.StartNew();
        ResultadoPayKlik? resultado = null;
        string codigoFalla = string.Empty;
        string motivoFalla = string.Empty;

        try
        {
            resultado = await _payklik.ReembolsoAsync(peticion, claveIdempotencia, cancelacion);
        }
        catch (CircuitoAbiertoException)
        {
            (codigoFalla, motivoFalla) =
                ("CIRCUITO_ABIERTO", "El disyuntor de PayKlik esta abierto; no se invoco al proveedor.");
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
                        ultimosCuatro: null,
                        marca: null,
                        latenciaMs,
                        ocurridoEn);
                    EncolarReembolsoAplicado(datos, resultado, ocurridoEn);
                    return;

                case EstadoRespuestaPayKlik.RECHAZADO:
                    intento.MarcarRechazado(
                        resultado.CodigoRespuesta, resultado.Mensaje, latenciaMs, ocurridoEn);
                    EncolarReembolsoFallido(datos, resultado.CodigoRespuesta, resultado.Mensaje, ocurridoEn);
                    return;
            }
        }

        // Sin respuesta util: fallo tecnico del proveedor o circuito abierto.
        intento.MarcarFallido(codigoFalla, motivoFalla, latenciaMs, ocurridoEn);

        _logger.LogWarning(
            "Reembolso de {DevolucionId} sin respuesta de PayKlik ({Codigo}); se emite ReembolsoFallido.",
            datos.DevolucionId, codigoFalla);

        EncolarReembolsoFallido(datos, codigoFalla, motivoFalla, ocurridoEn);
    }

    private void EncolarReembolsoAplicado(
        DevolucionRegistradaData datos,
        ResultadoPayKlik resultado,
        DateTime ocurridoEn)
    {
        _outbox.Encolar(
            ReembolsoAplicado.EventType,
            ReembolsoAplicado.AggregateType,
            datos.DevolucionId,
            sequence: 1,
            TopologiaMensajeria.ExchangeEventos,
            ReembolsoAplicado.RoutingKey,
            new ReembolsoAplicadoData(
                DevolucionId: datos.DevolucionId,
                OrdenOriginalId: datos.OrdenOriginalId,
                ClienteId: datos.ClienteId,
                Moneda: Enum.Parse<Moneda>(datos.Moneda),
                Monto: decimal.Parse(datos.MontoReembolso, CultureInfo.InvariantCulture),
                CodigoRespuesta: resultado.CodigoRespuesta,
                AutorizacionId: resultado.AutorizacionId ?? string.Empty,
                OcurridoEn: ocurridoEn));
    }

    private void EncolarReembolsoFallido(
        DevolucionRegistradaData datos,
        string codigoRespuesta,
        string motivoFalla,
        DateTime ocurridoEn)
    {
        _outbox.Encolar(
            ReembolsoFallido.EventType,
            ReembolsoFallido.AggregateType,
            datos.DevolucionId,
            sequence: 1,
            TopologiaMensajeria.ExchangeEventos,
            ReembolsoFallido.RoutingKey,
            new ReembolsoFallidoData(
                DevolucionId: datos.DevolucionId,
                OrdenOriginalId: datos.OrdenOriginalId,
                ClienteId: datos.ClienteId,
                Moneda: Enum.Parse<Moneda>(datos.Moneda),
                Monto: decimal.Parse(datos.MontoReembolso, CultureInfo.InvariantCulture),
                CodigoRespuesta: codigoRespuesta,
                MotivoFalla: motivoFalla,
                OcurridoEn: ocurridoEn));
    }
}
