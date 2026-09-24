using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;

namespace SgoLogic.MockMensajeria.Controllers;

public sealed record EnviarMensajeRequest(
    string Destinatario,
    string? Asunto,
    string Cuerpo);

public sealed record MensajeGenericoRequest(
    string? Destinatario,
    string? Asunto,
    string? Cuerpo,
    string? ClienteId,
    string? EstadoOrden,
    string? OrdenId);

public sealed record EnviarMensajeResponse(
    string MensajeId,
    string Canal,
    string Estado,
    string? Error);

/// <summary>
/// Mock del proveedor externo de mensajeria (SMS, email, push).
/// Permite simular envios exitosos o fallidos segun la variable de entorno
/// MockMensajeria:ForzarFalla, o realizar despachos reales via SMTP si esta configurado.
/// </summary>
[ApiController]
[Route("api/mensajes")]
public sealed class MensajesController : ControllerBase
{
    private static readonly ConcurrentQueue<object> _mensajesEnviados = new();
    private static bool? _forzarFallaOverride;
    private readonly IConfiguration _config;
    private readonly bool _forzarFallaConfig;
    private readonly ILogger<MensajesController> _logger;

    private bool ForzarFalla => _forzarFallaOverride ?? _forzarFallaConfig;

    public MensajesController(IConfiguration config, ILogger<MensajesController> logger)
    {
        _config = config;
        _forzarFallaConfig = config.GetValue<bool>("MockMensajeria:ForzarFalla");
        _logger = logger;
    }

    /// <summary>Retorna los mensajes enviados registrados en memoria.</summary>
    [HttpGet]
    public IActionResult ObtenerMensajes()
    {
        return Ok(_mensajesEnviados.ToArray());
    }

    /// <summary>Permite activar o desactivar la falla forzada dinamicamente para pruebas de resiliencia.</summary>
    [HttpPost("config/falla")]
    public IActionResult ConfigurarFalla([FromQuery] bool activa)
    {
        _forzarFallaOverride = activa;
        _logger.LogWarning("MockMensajeria: ForzarFalla sobreescrito dinamicamente a {Activa}", activa);
        return Ok(new { forzarFalla = activa });
    }

    /// <summary>Endpoint generico para envio de notificaciones (compatible con pruebas y suites de integracion).</summary>
    [HttpPost]
    [ProducesResponseType<EnviarMensajeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult EnviarGenerico([FromBody] MensajeGenericoRequest request)
    {
        var destinatario = request.Destinatario ?? request.ClienteId ?? "desconocido";
        var asunto = request.Asunto ?? (request.OrdenId != null ? $"Orden {request.OrdenId}" : "Notificacion SGO");
        var cuerpo = request.Cuerpo ?? (request.EstadoOrden != null ? $"Estado: {request.EstadoOrden}" : "(mensaje vacio)");

        return Enviar("GENERAL", new EnviarMensajeRequest(destinatario, asunto, cuerpo));
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
                "MOCK [{Canal}] -- FALLA FORZADA para {Destinatario} | MensajeId: {Id}",
                canal, request.Destinatario, mensajeId);

            return StatusCode(StatusCodes.Status500InternalServerError, new EnviarMensajeResponse(
                MensajeId: mensajeId,
                Canal: canal,
                Estado: "ERROR",
                Error: "Falla forzada para demo (MockMensajeria:ForzarFalla=true)"));
        }

        _logger.LogInformation(
            "MOCK [{Canal}] ENVIADO -> {Destinatario} | Asunto: {Asunto} | Cuerpo: {Cuerpo} | MensajeId: {Id}",
            canal, request.Destinatario, request.Asunto ?? "(sin asunto)", request.Cuerpo, mensajeId);

        _mensajesEnviados.Enqueue(new
        {
            MensajeId = mensajeId,
            Canal = canal,
            Destinatario = request.Destinatario,
            Asunto = request.Asunto,
            Cuerpo = request.Cuerpo,
            Fecha = DateTime.UtcNow
        });

        if (canal == "EMAIL" || (request.Destinatario != null && request.Destinatario.Contains('@')))
        {
            _ = Task.Run(() => IntentarEnvioSmtpReal(request.Destinatario, request.Asunto, request.Cuerpo));
        }

        return Ok(new EnviarMensajeResponse(
            MensajeId: mensajeId,
            Canal: canal,
            Estado: "ENVIADO",
            Error: null));
    }

    private void IntentarEnvioSmtpReal(string destinatario, string? asunto, string cuerpo)
    {
        var smtpHost = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(destinatario) || !destinatario.Contains('@'))
        {
            return;
        }

        try
        {
            var port = _config.GetValue<int>("Smtp:Port", 587);
            var user = _config["Smtp:User"]?.Trim().Trim('"').Trim('\'');
            var pass = _config["Smtp:Password"]?.Trim().Trim('"').Trim('\'');
            if (!string.IsNullOrWhiteSpace(pass) && smtpHost.Contains("gmail", StringComparison.OrdinalIgnoreCase))
            {
                pass = pass.Replace(" ", "");
            }
            var enableSsl = _config.GetValue<bool>("Smtp:EnableSsl", true);
            var from = _config["Smtp:From"]?.Trim().Trim('"').Trim('\'');
            if (string.IsNullOrWhiteSpace(from))
            {
                from = !string.IsNullOrWhiteSpace(user) ? user : "no-reply@megastore.gt";
            }

            using var client = new SmtpClient(smtpHost, port)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 15000
            };

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                client.Credentials = new NetworkCredential(user, pass);
            }

            var htmlBody = ConstruirCuerpoHtml(asunto, cuerpo);

            using var mail = new MailMessage
            {
                From = new MailAddress(from, "MegaStore Omnicanal", System.Text.Encoding.UTF8),
                Subject = asunto ?? "Notificacion MegaStore",
                SubjectEncoding = System.Text.Encoding.UTF8,
                Body = htmlBody,
                BodyEncoding = System.Text.Encoding.UTF8,
                IsBodyHtml = true
            };

            var plainView = AlternateView.CreateAlternateViewFromString(cuerpo, System.Text.Encoding.UTF8, "text/plain");
            var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, System.Text.Encoding.UTF8, "text/html");
            mail.AlternateViews.Add(plainView);
            mail.AlternateViews.Add(htmlView);
            mail.To.Add(destinatario);

            client.Send(mail);
            _logger.LogInformation("SMTP REAL: Correo enviado exitosamente a {Destinatario} via {Host}:{Port}", destinatario, smtpHost, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP REAL: Error al intentar enviar correo a {Destinatario} via {Host}: {Mensaje}", destinatario, smtpHost, ex.Message);
        }
    }

    private static string ConstruirCuerpoHtml(string? asunto, string cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo)) return "<p></p>";
        if (cuerpo.Contains("<html", StringComparison.OrdinalIgnoreCase)) return cuerpo;

        var titulo = asunto ?? "Notificacion MegaStore";
        var esCodigoVerificacion = cuerpo.Contains("codigo de verificacion", StringComparison.OrdinalIgnoreCase) ||
                                  cuerpo.Contains("código de verificación", StringComparison.OrdinalIgnoreCase);

        string bloqueDinamico;

        if (esCodigoVerificacion)
        {
            var matchCodigo = System.Text.RegularExpressions.Regex.Match(cuerpo, @"\b\d{6}\b");
            var codigoOtp = matchCodigo.Success ? matchCodigo.Value : "------";

            bloqueDinamico = $@"
              <div style=""text-align: center; margin: 28px 0;"">
                <div style=""display: inline-block; background-color: #f0fdf4; border: 2px dashed #16a34a; border-radius: 16px; padding: 24px 40px;"">
                  <div style=""font-size: 11px; font-weight: 700; color: #15803d; text-transform: uppercase; letter-spacing: 1.5px; margin-bottom: 8px;"">Codigo de Verificacion</div>
                  <div style=""font-size: 38px; font-weight: 800; font-family: 'Courier New', Courier, monospace; letter-spacing: 10px; color: #0f172a;"">{codigoOtp}</div>
                  <div style=""font-size: 12px; color: #64748b; margin-top: 8px;"">Valido durante 15 minutos</div>
                </div>
              </div>
              <p style=""font-size: 14px; color: #334155; line-height: 1.6; margin-bottom: 20px;"">{WebUtility.HtmlEncode(cuerpo)}</p>
              <div style=""background-color: #f8fafc; border-left: 4px solid #0284c7; padding: 12px 16px; border-radius: 4px; margin-top: 24px;"">
                <p style=""margin: 0; font-size: 12px; color: #64748b;""><strong>Seguridad:</strong> Si tu no solicitaste este codigo, puedes ignorar este correo de forma segura. Tu cuenta no sufrira ningun cambio.</p>
              </div>";
        }
        else if (cuerpo.Contains("orden", StringComparison.OrdinalIgnoreCase) && cuerpo.Contains("estado", StringComparison.OrdinalIgnoreCase))
        {
            bloqueDinamico = $@"
              <div style=""background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 16px; padding: 24px; margin: 24px 0;"">
                <div style=""font-size: 11px; font-weight: 700; color: #64748b; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 8px;"">Actualizacion de Pedido</div>
                <div style=""font-size: 16px; font-weight: 600; color: #0f172a; line-height: 1.5;"">{WebUtility.HtmlEncode(cuerpo)}</div>
              </div>
              <div style=""text-align: center; margin-top: 28px;"">
                <a href=""http://localhost:8092/Estado"" style=""display: inline-block; background-color: #0f172a; color: #ffffff; text-decoration: none; padding: 12px 28px; border-radius: 9999px; font-weight: 600; font-size: 13px;"">Consultar Mis Pedidos</a>
              </div>";
        }
        else
        {
            bloqueDinamico = $@"
              <div style=""font-size: 15px; color: #334155; line-height: 1.6; margin: 24px 0;"">
                <p style=""margin: 0;"">{WebUtility.HtmlEncode(cuerpo)}</p>
              </div>";
        }

        return $@"<!DOCTYPE html>
<html lang=""es"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{WebUtility.HtmlEncode(titulo)}</title>
</head>
<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased; color: #1e293b;"">
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background-color: #f1f5f9; padding: 40px 15px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" style=""max-width: 580px; background-color: #ffffff; border-radius: 24px; overflow: hidden; box-shadow: 0 4px 16px rgba(0, 0, 0, 0.06); border: 1px solid #e2e8f0;"" cellspacing=""0"" cellpadding=""0"">
          <tr>
            <td style=""background-color: #0f172a; padding: 30px 40px;"">
              <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"">
                <tr>
                  <td>
                    <div style=""font-size: 22px; font-weight: 800; color: #ffffff; letter-spacing: -0.5px;"">MegaStore</div>
                    <div style=""font-size: 11px; font-weight: 600; color: #94a3b8; text-transform: uppercase; letter-spacing: 1.5px; margin-top: 2px;"">Plataforma Omnicanal</div>
                  </td>
                  <td align=""right"">
                    <span style=""display: inline-block; background-color: rgba(255, 255, 255, 0.12); color: #38bdf8; font-size: 11px; font-weight: 700; padding: 4px 12px; border-radius: 9999px; text-transform: uppercase; letter-spacing: 0.5px;"">Oficial</span>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style=""padding: 36px 40px;"">
              <h1 style=""font-size: 20px; font-weight: 700; color: #0f172a; margin: 0 0 16px 0; letter-spacing: -0.5px;"">{WebUtility.HtmlEncode(titulo)}</h1>
              {bloqueDinamico}
            </td>
          </tr>
          <tr>
            <td style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 24px 40px; text-align: center;"">
              <p style=""margin: 0; font-size: 11px; color: #94a3b8; line-height: 1.5;"">MegaStore Omnicanal S.A. &bull; Ciudad de Guatemala<br>Este es un correo automatizado de seguridad y servicio al cliente.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }
}
