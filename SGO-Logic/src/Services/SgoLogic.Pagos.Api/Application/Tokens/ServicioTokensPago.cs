using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.Pagos.Api.Application.Tarjetas;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tokens;

/// <summary>
/// Tokenizacion interna (A2/A3): Pagos valida la tarjeta, cifra los datos sensibles y
/// devuelve un token opaco. No llama a PayKlik: PayKlik solo recibe los datos al
/// cobrar (<see cref="Pagos.ServicioCobro" />).
///
/// Nada de lo que entra aqui se registra: ni el numero, ni el CVV, ni los datos
/// descifrados. Los logs solo llevan marca, ultimos cuatro e id del token.
/// </summary>
public sealed class ServicioTokensPago
{
    private const int LoteExpiracion = 500;

    private readonly IRepositorioTokensPago _tokens;
    private readonly IRepositorioTarjetasGuardadas _tarjetas;
    private readonly ICifradorDatosTarjeta _cifrador;
    private readonly BovedaOptions _opciones;
    private readonly ILogger<ServicioTokensPago> _logger;
    private readonly Func<DateTime> _reloj;

    public ServicioTokensPago(
        IRepositorioTokensPago tokens,
        IRepositorioTarjetasGuardadas tarjetas,
        ICifradorDatosTarjeta cifrador,
        IOptions<BovedaOptions> opciones,
        ILogger<ServicioTokensPago> logger)
        : this(tokens, tarjetas, cifrador, opciones, logger, () => DateTime.UtcNow)
    {
    }

    public ServicioTokensPago(
        IRepositorioTokensPago tokens,
        IRepositorioTarjetasGuardadas tarjetas,
        ICifradorDatosTarjeta cifrador,
        IOptions<BovedaOptions> opciones,
        ILogger<ServicioTokensPago> logger,
        Func<DateTime> reloj)
    {
        _tokens = tokens;
        _tarjetas = tarjetas;
        _cifrador = cifrador;
        _opciones = opciones.Value;
        _logger = logger;
        _reloj = reloj;
    }

    /// <summary>
    /// Valida la tarjeta con <see cref="ValidadorTarjeta" /> y, si es valida, la
    /// guarda cifrada en un token nuevo. <paramref name="clienteId" /> es el email del
    /// JWT (null para invitados): guardar=true solo se respeta con cliente.
    /// </summary>
    public async Task<ResultadoTokenizacion> TokenizarAsync(
        SolicitudTokenPago solicitud,
        string? clienteId,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var ahora = _reloj();
        var validacion = ValidadorTarjeta.Validar(
            new DatosTarjeta(solicitud.Numero, solicitud.MesExp, solicitud.AnioExp, solicitud.Cvv, solicitud.Titular),
            ahora);

        if (!validacion.EsValida)
        {
            return new ResultadoTokenizacion(null, validacion.Errores);
        }

        var tarjeta = validacion.Tarjeta!;
        var id = TokenPago.GenerarId();
        var cifrado = _cifrador.Cifrar(new SecretoTarjeta(tarjeta.Numero, tarjeta.Cvv), id);

        var token = TokenPago.DesdeTarjetaNueva(
            id,
            clienteId,
            tarjeta,
            cifrado,
            guardarAlCobrar: solicitud.Guardar && !string.IsNullOrWhiteSpace(clienteId),
            ahora,
            _opciones.TokenTtl);

        await _tokens.AgregarAsync(token, cancelacion);
        await _tokens.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Token {TokenId} emitido para tarjeta {Marca} terminada en {Ultimos4} (guardar al cobrar: {Guardar}).",
            token.Id, token.Marca, token.Ultimos4, token.GuardarAlCobrar);

        return new ResultadoTokenizacion(Desde(token), new Dictionary<string, string>());
    }

    /// <summary>
    /// Token desde una tarjeta guardada del cliente: verifica dueno, vencimiento y CVV
    /// por marca, y cifra solo el CVV. Una tarjeta de otro cliente se trata igual que
    /// una inexistente.
    /// </summary>
    public async Task<ResultadoTokenTarjetaGuardada> TokenizarTarjetaGuardadaAsync(
        string clienteId,
        Guid tarjetaId,
        string? cvv,
        CancellationToken cancelacion)
    {
        var tarjeta = await _tarjetas.ObtenerActivaAsync(tarjetaId, cancelacion);
        if (tarjeta is null || !string.Equals(tarjeta.ClienteId, clienteId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new ResultadoTokenTarjetaGuardada(EstadoTokenTarjetaGuardada.NoEncontrada, Mensaje: "La tarjeta no existe.");
        }

        var ahora = _reloj();
        if (tarjeta.EstaVencida(ahora))
        {
            return new ResultadoTokenTarjetaGuardada(EstadoTokenTarjetaGuardada.Vencida, Mensaje: "La tarjeta esta vencida.");
        }

        var cvvLimpio = cvv?.Trim();
        if (!ReglasTarjeta.CvvValido(tarjeta.Marca, cvvLimpio))
        {
            return new ResultadoTokenTarjetaGuardada(
                EstadoTokenTarjetaGuardada.CvvInvalido,
                Mensaje: $"El codigo de seguridad debe tener {ReglasTarjeta.LargoCvv(tarjeta.Marca)} digitos.");
        }

        var id = TokenPago.GenerarId();
        var cifrado = _cifrador.Cifrar(new SecretoTarjeta(null, cvvLimpio!), id);
        var token = TokenPago.DesdeTarjetaGuardada(id, tarjeta.ClienteId, tarjeta, cifrado, ahora, _opciones.TokenTtl);

        await _tokens.AgregarAsync(token, cancelacion);
        tarjeta.RegistrarUso(ahora);
        await _tokens.GuardarCambiosAsync(cancelacion);

        _logger.LogInformation(
            "Token {TokenId} emitido desde la tarjeta guardada {TarjetaId} ({Marca} terminada en {Ultimos4}).",
            token.Id, tarjeta.Id, tarjeta.Marca, tarjeta.Ultimos4);

        return new ResultadoTokenTarjetaGuardada(EstadoTokenTarjetaGuardada.Emitido, Desde(token));
    }

    /// <summary>
    /// Marca EXPIRADO los tokens activos vencidos y borra sus datos cifrados. Lo llama
    /// el worker de expiracion; devuelve cuantos expiro.
    /// </summary>
    public async Task<int> ExpirarVencidosAsync(CancellationToken cancelacion)
    {
        var vencidos = await _tokens.ListarActivosVencidosAsync(_reloj(), LoteExpiracion, cancelacion);
        if (vencidos.Count == 0)
        {
            return 0;
        }

        foreach (var token in vencidos)
        {
            token.Expirar();
        }

        await _tokens.GuardarCambiosAsync(cancelacion);
        return vencidos.Count;
    }

    public static TokenPagoDto Desde(TokenPago token) =>
        new(token.Id, token.Marca.ToString(), token.Ultimos4, token.ExpiraEn);
}
