namespace SgoLogic.Auth.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="HttpMessageHandler" /> para el cliente HTTP de Inventario:
/// responde lo que se le configure o lanza, como si el servicio estuviera caido.
/// </summary>
public sealed class ManejadorHttpFalso : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public ManejadorHttpFalso(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Solicitudes { get; } = new();

    public static ManejadorHttpFalso Caido() =>
        new(_ => throw new HttpRequestException("No connection could be made: inventario-api:8080"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Solicitudes.Add(request);
        return Task.FromResult(_responder(request));
    }
}
