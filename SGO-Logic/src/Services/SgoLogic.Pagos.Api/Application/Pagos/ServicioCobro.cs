using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Nucleo de HU-004. Procesa un cobro contra PayKlik y encola el evento de
/// resultado. Lo usan el consumidor de <c>ordenes.cobro.solicitado</c> y el
/// endpoint de reintento.
///
/// No llama a <c>SaveChangesAsync</c>: la transaccion la cierra quien llama
/// (el consumidor para el consumo, el controlador para el reintento), de modo
/// que el intento, el cambio de estado del pago, el enlace, el token, la fila de
/// outbox y (en el consumo) la marca de mensaje procesado quedan en una sola
/// transaccion.
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
///
/// Tokenizacion interna (A4): el cobro con TARJETA llega con el id de un
/// <see cref="TokenPago" /> de la boveda transitoria. Este servicio lo resuelve,
/// descifra en memoria el PAN y el CVV (o solo el CVV de una tarjeta guardada) y se
/// los manda a PayKlik. Un token inexistente, vencido, ya consumido o ilegible
/// termina en pago RECHAZADO con un codigo TOKEN_* (<see cref="CodigosToken" />)
/// sin llamar a PayKlik: es un error del cliente, no una falla del proveedor.
///
/// El token pasa a CONSUMIDO cuando PayKlik da una respuesta definitiva (aprobado,
/// rechazado o datos no aceptados). Si PayKlik no responde o el disyuntor esta
/// abierto sigue ACTIVO hasta su vencimiento, para que el reintento cobre sin pedir
/// datos otra vez. Los datos descifrados nunca se escriben en logs, excepciones ni
/// eventos.
/// </summary>
public sealed class ServicioCobro
{
    private readonly PagosDbContext _contexto;
    private readonly IClientePayKlik _payklik;
    private readonly IOutboxWriter _outbox;
    private readonly PayKlikOptions _payklikOpciones;
    private readonly ReintentoOptions _reintento;
    private readonly ICifradorDatosTarjeta _cifrador;
    private readonly ILogger<ServicioCobro> _logger;

    public ServicioCobro(
        PagosDbContext contexto,
        IClientePayKlik payklik,
        IOutboxWriter outbox,
        IOptions<PayKlikOptions> payklikOpciones,
        IOptions<ReintentoOptions> reintentoOpciones,
        ICifradorDatosTarjeta cifrador,
        ILogger<ServicioCobro> logger)
    {
        _contexto = contexto;
        _payklik = payklik;
        _outbox = outbox;
        _payklikOpciones = payklikOpciones.Value;
        _reintento = reintentoOpciones.Value;
        _cifrador = cifrador;
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

        MedioCobroTarjeta? medio = null;

        if (solicitud.MedioPago == MedioPago.TARJETA)
        {
            // Un cobro con TARJETA sin token no se puede cobrar. Se rechaza sin llamar
            // al proveedor (no es una falla suya ni pasa por el disyuntor) y el rechazo
            // llega a Ordenes por PagoRechazado para que la saga compense.
            if (string.IsNullOrWhiteSpace(solicitud.TokenMedioPago))
            {
                _logger.LogWarning(
                    "Cobro de {OrdenId} con TARJETA sin token de medio de pago; se rechaza sin invocar a PayKlik.",
                    pago.OrdenId);

                return RechazarSinInvocar(
                    pago,
                    solicitud,
                    tokenId: null,
                    token: null,
                    CodigosToken.Requerido,
                    "El cobro con TARJETA requiere un token de medio de pago; no se invoco a PayKlik.");
            }

            var resolucion = await ResolverTokenAsync(solicitud.TokenMedioPago, pago, cancelacion);
            if (resolucion.CodigoRechazo is not null)
            {
                _logger.LogWarning(
                    "Cobro de {OrdenId} rechazado sin invocar a PayKlik: token de medio de pago no utilizable ({Codigo}).",
                    pago.OrdenId, resolucion.CodigoRechazo);

                return RechazarSinInvocar(
                    pago,
                    solicitud,
                    solicitud.TokenMedioPago,
                    resolucion.Token,
                    resolucion.CodigoRechazo,
                    resolucion.Mensaje!);
            }

            medio = resolucion.Medio;
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
                solicitud.MedioPago,
                solicitud.TokenMedioPago,
                medio?.Token.Ultimos4 ?? solicitud.UltimosCuatro,
                medio?.Token.Marca.ToString() ?? solicitud.Marca);
            _contexto.Intentos.Add(intento);

            var peticion = new PeticionCargoPayKlik(
                pago.OrdenId,
                pago.Monto.ToString("0.00", CultureInfo.InvariantCulture),
                pago.Moneda,
                pago.MedioPago,
                Tarjeta: medio?.Tarjeta,
                Guardar: medio?.Token.GuardarAlCobrar == true ? true : null,
                ReferenciaTarjeta: medio?.TarjetaGuardada?.ReferenciaPayKlik,
                Cvv: medio?.TarjetaGuardada is not null ? medio.Cvv : null);

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
            catch (TokenMedioPagoInvalidoException ex)
            {
                // PayKlik no acepto los datos del medio de pago. Esta arriba y respondio
                // bien, asi que no es una falla del proveedor: no se reintenta, el
                // disyuntor no lo cuenta y el pago queda RECHAZADO con el codigo.
                cronometro.Stop();
                var ocurridoConDatosRechazados = DateTime.UtcNow;

                _logger.LogWarning(
                    "Cobro de {OrdenId} rechazado por PayKlik: medio de pago no aceptado ({Codigo}).",
                    pago.OrdenId, ex.Codigo);

                // La referencia de la tarjeta guardada ya no existe en PayKlik: el
                // cliente tiene que agregarla de nuevo.
                if (ex.Codigo == CodigosToken.TarjetaNoEncontrada && medio?.TarjetaGuardada is { } tarjetaPerdida)
                {
                    tarjetaPerdida.Desactivar();
                    _logger.LogWarning(
                        "PayKlik ya no reconoce la tarjeta guardada {TarjetaId} (terminada en {Ultimos4}); se desactivo.",
                        tarjetaPerdida.Id, tarjetaPerdida.Ultimos4);
                }

                medio?.Token.Consumir();
                intento.MarcarRechazado(
                    ex.Codigo, ex.Message, (int)cronometro.ElapsedMilliseconds, ocurridoConDatosRechazados);
                pago.MarcarRechazado(ocurridoConDatosRechazados);
                EncolarPagoRechazado(pago, intento, ex.Codigo, ocurridoConDatosRechazados);
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
                        if (medio is not null)
                        {
                            await CerrarTokenCobradoAsync(medio, resultado, ocurridoEn, cancelacion);
                        }

                        EncolarPagoAplicado(pago, intento, resultado, ocurridoEn);
                        return pago;

                    case EstadoRespuestaPayKlik.RECHAZADO:
                        intento.MarcarRechazado(
                            resultado.CodigoRespuesta,
                            resultado.Mensaje,
                            latenciaMs,
                            ocurridoEn,
                            resultado.UltimosCuatro,
                            resultado.Marca);
                        pago.MarcarRechazado(ocurridoEn);
                        medio?.Token.Consumir();
                        EncolarPagoRechazado(pago, intento, resultado.CodigoRespuesta, ocurridoEn);
                        return pago;
                }
            }

            // Sin respuesta util: fallo tecnico del proveedor o circuito abierto. El
            // token se queda ACTIVO para que el reintento lo use sin pedir datos.
            intento.MarcarFallido(codigoFalla, motivoFalla, latenciaMs, ocurridoEn);

            // EAC-01: cada intento fallido se audita, tambien los intermedios que el
            // reintento supera. Va por el mismo outbox y en la misma transaccion que
            // el intento; no cambia el evento de resultado del cobro.
            var puedeReintentar = !circuitoAbierto && numeroLlamada < maxLlamadas;
            EncolarIntentoPagoFallido(pago, intento, codigoFalla, motivoFalla, latenciaMs, puedeReintentar, ocurridoEn);
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
                "Cobro de {OrdenId} sin respuesta de PayKlik ({Codigo}: {MotivoFalla}); el pago queda PENDIENTE.",
                pago.OrdenId, codigoFalla, motivoFalla);

            pago.MarcarPendiente(ocurridoEn);

            var enlace = pago.GenerarEnlaceReintento(_reintento.Expiracion, ocurridoEn);
            _contexto.EnlacesReintento.Add(enlace);

            EncolarPagoPendiente(pago, intento, codigoFalla, motivoFalla, enlace, ocurridoEn);
            return pago;
        }

        // Inalcanzable: el bucle siempre retorna dentro de alguna de sus ramas.
        throw new InvalidOperationException("El bucle de cobro no produjo un resultado.");
    }

    /// <summary>
    /// Que datos hacen falta para cobrar con el token, sin cobrar ni modificar nada:
    /// null si el token sirve tal cual; si no, el codigo TOKEN_* que lo invalida. Lo
    /// usa el reintento para decidir si pide otra vez los datos de la tarjeta.
    /// </summary>
    public async Task<string?> DiagnosticarTokenAsync(string? tokenId, string clienteId, CancellationToken cancelacion)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return CodigosToken.Requerido;
        }

        var token = await _contexto.TokensPago.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tokenId, cancelacion);
        if (token is null || !token.PerteneceA(clienteId))
        {
            return CodigosToken.Invalido;
        }

        if (token.Estado == EstadoTokenPago.CONSUMIDO)
        {
            return CodigosToken.YaUsado;
        }

        return token.EstaVigente(DateTime.UtcNow) ? null : CodigosToken.Vencido;
    }

    /// <summary>
    /// Resuelve el token de la boveda y descifra sus datos en memoria. No llama a
    /// PayKlik. Si el token no sirve devuelve el codigo de rechazo; un token vencido
    /// se marca EXPIRADO aqui mismo (y se borran sus datos).
    /// </summary>
    private async Task<ResolucionToken> ResolverTokenAsync(string tokenId, Pago pago, CancellationToken cancelacion)
    {
        var token = await _contexto.TokensPago.FirstOrDefaultAsync(t => t.Id == tokenId, cancelacion);

        if (token is null || !token.PerteneceA(pago.ClienteId))
        {
            return ResolucionToken.Rechazo(
                CodigosToken.Invalido, "El token de medio de pago no existe.", token: null);
        }

        if (token.Estado == EstadoTokenPago.CONSUMIDO)
        {
            return ResolucionToken.Rechazo(
                CodigosToken.YaUsado, "El token de medio de pago ya se uso en otro cobro.", token);
        }

        if (token.Estado == EstadoTokenPago.EXPIRADO || token.EstaVencido(DateTime.UtcNow))
        {
            token.Expirar();
            return ResolucionToken.Rechazo(
                CodigosToken.Vencido, "El token de medio de pago vencio; hay que ingresar la tarjeta de nuevo.", token);
        }

        if (!token.TieneDatosCifrados)
        {
            return ResolucionToken.Rechazo(
                CodigosToken.Invalido, "El token de medio de pago no tiene datos de tarjeta.", token);
        }

        SecretoTarjeta secreto;
        try
        {
            secreto = _cifrador.Descifrar(token.ObtenerDatosCifrados(), token.Id);
        }
        catch (CryptographicException)
        {
            // Datos alterados u otra clave: el mensaje no incluye nada del contenido.
            _logger.LogError("Los datos cifrados del token {TokenId} no superaron la verificacion de integridad.", token.Id);
            return ResolucionToken.Rechazo(
                CodigosToken.Invalido, "Los datos del token de medio de pago no se pudieron verificar.", token);
        }

        if (token.ReferenciaTarjetaGuardadaId is Guid tarjetaId)
        {
            var tarjeta = await _contexto.TarjetasGuardadas
                .FirstOrDefaultAsync(t => t.Id == tarjetaId && t.Activa, cancelacion);

            if (tarjeta is null)
            {
                token.Consumir();
                return ResolucionToken.Rechazo(
                    CodigosToken.TarjetaNoValida, "La tarjeta guardada ya no es valida; agregala de nuevo.", token);
            }

            return ResolucionToken.Ok(new MedioCobroTarjeta(token, Tarjeta: null, tarjeta, secreto.Cvv));
        }

        if (string.IsNullOrEmpty(secreto.Numero))
        {
            return ResolucionToken.Rechazo(
                CodigosToken.Invalido, "El token de medio de pago no tiene datos de tarjeta.", token);
        }

        var datosTarjeta = new TarjetaCargoPayKlik(secreto.Numero, token.MesExp, token.AnioExp, secreto.Cvv, token.Titular);
        return ResolucionToken.Ok(new MedioCobroTarjeta(token, datosTarjeta, TarjetaGuardada: null, secreto.Cvv));
    }

    /// <summary>
    /// Cobro aprobado: el token se consume (se borran sus datos) y, si se pidio
    /// guardar la tarjeta y PayKlik devolvio su referencia, se crea o actualiza la
    /// tarjeta guardada del cliente. Con tarjeta guardada solo se registra su uso.
    /// </summary>
    private async Task CerrarTokenCobradoAsync(
        MedioCobroTarjeta medio,
        ResultadoPayKlik resultado,
        DateTime ahora,
        CancellationToken cancelacion)
    {
        var token = medio.Token;

        if (medio.TarjetaGuardada is not null)
        {
            medio.TarjetaGuardada.RegistrarUso(ahora);
        }
        else if (token.GuardarAlCobrar && token.ClienteId is not null)
        {
            if (string.IsNullOrWhiteSpace(resultado.ReferenciaTarjeta))
            {
                _logger.LogWarning(
                    "PayKlik aprobo el cobro con guardar=true sin devolver referencia; la tarjeta terminada en {Ultimos4} no se guardo.",
                    token.Ultimos4);
            }
            else
            {
                var (tarjeta, estado) = await ServicioTarjetasGuardadas.RegistrarAsync(
                    new RepositorioTarjetasGuardadas(_contexto),
                    token.ClienteId,
                    resultado.ReferenciaTarjeta,
                    token.Marca,
                    token.Ultimos4,
                    token.MesExp,
                    token.AnioExp,
                    token.Titular,
                    ahora,
                    cancelacion);

                tarjeta.RegistrarUso(ahora);
                _logger.LogInformation(
                    "Tarjeta {Marca} terminada en {Ultimos4} guardada al cobrar ({Estado}).",
                    token.Marca, token.Ultimos4, estado);
            }
        }

        token.Consumir();
    }

    private Pago RechazarSinInvocar(
        Pago pago,
        SolicitudCobro solicitud,
        string? tokenId,
        TokenPago? token,
        string codigo,
        string mensaje)
    {
        var ocurridoEn = DateTime.UtcNow;
        var intento = pago.RegistrarIntento(
            solicitud.MedioPago,
            tokenId,
            token?.Ultimos4 ?? solicitud.UltimosCuatro,
            token?.Marca.ToString() ?? solicitud.Marca);
        _contexto.Intentos.Add(intento);

        intento.MarcarRechazado(codigo, mensaje, latenciaMs: 0, ocurridoEn);
        pago.MarcarRechazado(ocurridoEn);
        EncolarPagoRechazado(pago, intento, codigo, ocurridoEn);
        return pago;
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

    private void EncolarPagoRechazado(Pago pago, IntentoPago intento, string codigoRespuesta, DateTime ocurridoEn) =>
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
                CodigoRespuesta: codigoRespuesta,
                OcurridoEn: ocurridoEn));

    // Solo para auditoria. Agregado IntentoPago con sequence = numero de intento:
    // no toca pago.Secuencia, que siguen usando PagoAplicado, PagoRechazado y
    // PagoPendiente.
    private void EncolarIntentoPagoFallido(
        Pago pago,
        IntentoPago intento,
        string codigoRespuesta,
        string motivoFalla,
        int latenciaMs,
        bool seReintenta,
        DateTime ocurridoEn) =>
        _outbox.Encolar(
            IntentoPagoFallido.EventType,
            IntentoPagoFallido.AggregateType,
            pago.OrdenId,
            intento.IntentoNumero,
            TopologiaMensajeria.ExchangeEventos,
            IntentoPagoFallido.RoutingKey,
            new IntentoPagoFallidoData(
                OrdenId: pago.OrdenId,
                ClienteId: pago.ClienteId,
                Canal: pago.Canal,
                Moneda: pago.Moneda,
                Monto: pago.Monto,
                MedioPago: pago.MedioPago,
                IntentoNumero: intento.IntentoNumero,
                CodigoRespuesta: codigoRespuesta,
                MotivoFalla: motivoFalla,
                LatenciaMs: latenciaMs,
                SeReintenta: seReintenta,
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

    /// <summary>
    /// Lo que se le manda a PayKlik con un token ya resuelto: los datos de una tarjeta
    /// nueva, o la tarjeta guardada mas el CVV. Solo vive durante el cobro.
    /// </summary>
    private sealed record MedioCobroTarjeta(
        TokenPago Token,
        TarjetaCargoPayKlik? Tarjeta,
        TarjetaGuardada? TarjetaGuardada,
        string Cvv)
    {
        public override string ToString() => nameof(MedioCobroTarjeta) + $" {{ token {Token.Id} }}";
    }

    private sealed record ResolucionToken(MedioCobroTarjeta? Medio, string? CodigoRechazo, string? Mensaje, TokenPago? Token)
    {
        public static ResolucionToken Ok(MedioCobroTarjeta medio) => new(medio, null, null, medio.Token);

        public static ResolucionToken Rechazo(string codigo, string mensaje, TokenPago? token) => new(null, codigo, mensaje, token);
    }
}
