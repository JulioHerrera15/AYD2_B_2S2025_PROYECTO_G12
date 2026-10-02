namespace SgoLogic.Pagos.Api.Application.Tokens;

/// <summary>
/// Seccion "Boveda" de la configuracion (boveda transitoria de tokens, A2). En
/// docker-compose: Boveda__ClaveCifrado = PAGOS_CLAVE_CIFRADO y
/// Boveda__TokenTtlMinutos = PAGOS_TOKEN_TTL_MINUTOS.
/// </summary>
public sealed class BovedaOptions
{
    public const string Seccion = "Boveda";

    /// <summary>Clave AES-256 en base64 (32 bytes). Sin ella, o invalida, el servicio no arranca.</summary>
    public string? ClaveCifrado { get; set; }

    /// <summary>
    /// Vigencia del token desde que se crea. 30 minutos por defecto: cubre la saga
    /// (la reserva de inventario dura 15) y los reintentos cortos con PayKlik caido.
    /// </summary>
    public int TokenTtlMinutos { get; set; } = 30;

    /// <summary>Cada cuanto el worker marca EXPIRADO los tokens vencidos y borra sus datos.</summary>
    public int IntervaloExpiracionSegundos { get; set; } = 60;

    public TimeSpan TokenTtl => TimeSpan.FromMinutes(Math.Max(1, TokenTtlMinutos));
}
