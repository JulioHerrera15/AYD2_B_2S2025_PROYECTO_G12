namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="HttpMessageHandler" /> para probar el cliente HTTP real de
/// PayKlik sin red: responde con lo que devuelva el delegado y registra cada
/// peticion recibida (ruta, header Idempotency-Key y cuerpo).
/// </summary>
public sealed class ManejadorHttpFalso : HttpMessageHandler
{
    public sealed record SolicitudCapturada(string Metodo, string Ruta, string? ClaveIdempotencia, string Cuerpo);

    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    private readonly object _bloqueo = new();
    private readonly List<SolicitudCapturada> _solicitudes = new();

    public ManejadorHttpFalso(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public int Llamadas
    {
        get
        {
            lock (_bloqueo)
            {
                return _solicitudes.Count;
            }
        }
    }

    public IReadOnlyList<SolicitudCapturada> Solicitudes
    {
        get
        {
            lock (_bloqueo)
            {
                return _solicitudes.ToArray();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var clave = request.Headers.TryGetValues("Idempotency-Key", out var valores) ? valores.FirstOrDefault() : null;

        lock (_bloqueo)
        {
            _solicitudes.Add(new SolicitudCapturada(
                request.Method.Method, request.RequestUri?.AbsolutePath ?? string.Empty, clave, cuerpo));
        }

        return _responder(request);
    }
}
