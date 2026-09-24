namespace SgoLogic.Notificaciones.Api.Application;

public interface IMockMensajeriaClient
{
    Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default);
    Task EnviarEmailAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default);
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
                $"El proveedor de mensajeria respondio {(int)response.StatusCode}",
                null,
                response.StatusCode);

        _logger.LogInformation(
            "Notificacion enviada a {Destinatario} | Asunto: {Asunto}",
            destinatario, asunto);
    }
}
