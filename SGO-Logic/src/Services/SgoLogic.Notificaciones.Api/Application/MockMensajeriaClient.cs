namespace SgoLogic.Notificaciones.Api.Application;

public interface IMockMensajeriaClient
{
    Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default);
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

    public async Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default)
    {
        var payload = new
        {
            Destinatario = clienteId,
            Asunto = $"Tu orden {ordenId} cambio de estado",
            Cuerpo = $"El estado de tu orden {ordenId} es ahora: {estadoOrden}"
        };

        var response = await _http.PostAsJsonAsync("/api/mensajes/email", payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"El proveedor de mensajeria respondio {(int)response.StatusCode}",
                null,
                response.StatusCode);

        _logger.LogInformation(
            "Notificacion enviada al cliente {ClienteId} para orden {OrdenId} — estado {Estado}",
            clienteId, ordenId, estadoOrden);
    }
}
