using Microsoft.AspNetCore.Mvc;

namespace SgoLogic.MockMensajeria.Controllers;

public sealed record EnviarMensajeRequest(
    string Destinatario,
    string? Asunto,
    string Cuerpo);

public sealed record EnviarMensajeResponse(
    string MensajeId,
    string Canal,
    string Estado,
    string? Error);

/// <summary>
/// Mock del proveedor externo de mensajeria (SMS, email, push).
/// Simula envios exitosos o fallidos segun la variable de entorno
/// MockMensajeria:ForzarFalla. Util para demostrar que Notificaciones
/// aisla correctamente la falla del proveedor sin afectar la saga de Ordenes.
/// </summary>
[ApiController]
[Route("api/mensajes")]
public sealed class MensajesController : ControllerBase
{
    private static bool? _forzarFallaOverride;
    private readonly bool _forzarFallaConfig;
    private readonly ILogger<MensajesController> _logger;

    private bool ForzarFalla => _forzarFallaOverride ?? _forzarFallaConfig;

    public MensajesController(IConfiguration config, ILogger<MensajesController> logger)
    {
        _forzarFallaConfig = config.GetValue<bool>("MockMensajeria:ForzarFalla");
        _logger = logger;
    }

    /// <summary>Permite activar o desactivar la falla forzada dinámicamente para pruebas de resiliencia.</summary>
    [HttpPost("config/falla")]
    public IActionResult ConfigurarFalla([FromQuery] bool activa)
    {
        _forzarFallaOverride = activa;
        _logger.LogWarning("MockMensajeria: ForzarFalla sobreescrito dinamicamente a {Activa}", activa);
        return Ok(new { forzarFalla = activa });
    }

    /// <summary>Simula envio de SMS.</summary>
    [HttpPost("sms")]
    [ProducesResponseType<EnviarMensajeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult EnviarSms([FromBody] EnviarMensajeRequest request)
        => Enviar("SMS", request);

    /// <summary>Simula envio de correo electronico.</summary>
    [HttpPost("email")]
    [ProducesResponseType<EnviarMensajeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult EnviarEmail([FromBody] EnviarMensajeRequest request)
        => Enviar("EMAIL", request);

    /// <summary>Simula envio de notificacion push.</summary>
    [HttpPost("push")]
    [ProducesResponseType<EnviarMensajeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult EnviarPush([FromBody] EnviarMensajeRequest request)
        => Enviar("PUSH", request);

    private IActionResult Enviar(string canal, EnviarMensajeRequest request)
    {
        var mensajeId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        if (ForzarFalla)
        {
            _logger.LogWarning(
                "MOCK [{Canal}] — FALLA FORZADA para {Destinatario} | MensajeId: {Id}",
                canal, request.Destinatario, mensajeId);

            return StatusCode(StatusCodes.Status500InternalServerError, new EnviarMensajeResponse(
                MensajeId: mensajeId,
                Canal: canal,
                Estado: "ERROR",
                Error: "Falla forzada para demo (MockMensajeria:ForzarFalla=true)"));
        }

        _logger.LogInformation(
            "MOCK [{Canal}] ENVIADO → {Destinatario} | Asunto: {Asunto} | Cuerpo: {Cuerpo} | MensajeId: {Id}",
            canal, request.Destinatario, request.Asunto ?? "(sin asunto)", request.Cuerpo, mensajeId);

        return Ok(new EnviarMensajeResponse(
            MensajeId: mensajeId,
            Canal: canal,
            Estado: "ENVIADO",
            Error: null));
    }
}
