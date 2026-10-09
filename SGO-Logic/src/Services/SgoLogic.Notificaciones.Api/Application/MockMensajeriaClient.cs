namespace SgoLogic.Notificaciones.Api.Application;

public interface IMockMensajeriaClient
{
    Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default);
    Task EnviarEmailAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default);
    Task EnviarSmsAsync(string destinatario, string cuerpo, CancellationToken ct = default) => Task.CompletedTask;
    Task EnviarPushAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default) => Task.CompletedTask;
    Task EnviarWebhookAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class MockMensajeriaClient : IMockMensajeriaClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MockMensajeriaClient> _logger;

    public MockMensajeriaClient(HttpClient http, ILogger<MockMensajeriaClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default)
    {
        return EnviarEmailAsync(
            destinatario: clienteId,
            asunto: $"Tu orden {ordenId} cambio de estado",
            cuerpo: $"El estado de tu orden {ordenId} es ahora: {estadoOrden}",
            ct: ct);
    }

    public async Task EnviarEmailAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
    {
        var payload = new
        {
            Destinatario = destinatario,
            Asunto = asunto,
            Cuerpo = cuerpo
        };

        var response = await _http.PostAsJsonAsync("/api/mensajes/email", payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"El proveedor de mensajeria (Email) respondio {(int)response.StatusCode}",
                null,
                response.StatusCode);

        _logger.LogInformation(
            "Notificacion Email enviada a {Destinatario} | Asunto: {Asunto}",
            destinatario, asunto);
    }

    public async Task EnviarSmsAsync(string destinatario, string cuerpo, CancellationToken ct = default)
    {
        var payload = new
        {
            Destinatario = destinatario,
            Asunto = "Aviso SMS SGO-Logic",
            Cuerpo = cuerpo
        };

        var response = await _http.PostAsJsonAsync("/api/mensajes/sms", payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"El proveedor de mensajeria (SMS) respondio {(int)response.StatusCode}",
                null,
                response.StatusCode);

        _logger.LogInformation(
            "Notificacion SMS enviada a {Destinatario} | {Cuerpo}",
            destinatario, cuerpo);
    }

    public async Task EnviarPushAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
    {
        var payload = new
        {
            Destinatario = destinatario,
            Asunto = asunto,
            Cuerpo = cuerpo
        };

        var response = await _http.PostAsJsonAsync("/api/mensajes/push", payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"El proveedor de mensajeria (Push) respondio {(int)response.StatusCode}",
                null,
                response.StatusCode);

        _logger.LogInformation(
            "Notificacion Push enviada a {Destinatario} | Asunto: {Asunto}",
            destinatario, asunto);
    }

    public async Task EnviarWebhookAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
    {
        var payload = new
        {
            Destinatario = destinatario,
            Asunto = asunto,
            Cuerpo = cuerpo,
            Canal = "WEBHOOK",
            EnviadoEn = DateTime.UtcNow
        };

        var response = await _http.PostAsJsonAsync("/api/mensajes", payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"El webhook externo respondio {(int)response.StatusCode}",
                null,
                response.StatusCode);

        _logger.LogInformation(
            "Notificacion Webhook disparada a {Destinatario} | Asunto: {Asunto}",
            destinatario, asunto);
    }
}
