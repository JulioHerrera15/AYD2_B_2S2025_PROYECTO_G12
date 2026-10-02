using System.Net.Http.Headers;

namespace SgoLogic.Web.Services;

/// <summary>
/// Pone en cada <see cref="HttpRequestMessage" /> hacia el Gateway los encabezados de
/// la sesion del navegador que origino la peticion: Authorization (cookie sgo_jwt) y
/// X-Session-Id (cookie sgo_guest_id). Si la cookie aun no existe en la peticion
/// (justo despues del login) toma el valor de HttpContext.Items.
///
/// Reemplaza a la vieja escritura sobre HttpClient.DefaultRequestHeaders, que no es
/// segura entre hilos: con varias llamadas en paralelo (Task.WhenAll) una peticion
/// podia salir con los encabezados de otra o fallar al modificar la coleccion. Aqui
/// cada mensaje lleva los suyos.
///
/// Una peticion que ya trae su propio Authorization o X-Session-Id (por ejemplo,
/// asociar el carrito con el token recien emitido) se respeta tal cual.
/// </summary>
public sealed class EncabezadosSesionHandler : DelegatingHandler
{
    public const string CookieJwt = "sgo_jwt";
    public const string CookieInvitado = "sgo_guest_id";
    public const string EncabezadoSesion = "X-Session-Id";

    /// <summary>Marca un mensaje que no debe llevar la sesion del navegador (consulta a nombre de otro).</summary>
    public static readonly HttpRequestOptionsKey<bool> SinSesion = new("sgo.sin-sesion");

    private readonly IHttpContextAccessor _httpContextAccessor;

    public EncabezadosSesionHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Aplicar(request, _httpContextAccessor.HttpContext);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Copia la sesion de <paramref name="contexto" /> al mensaje, sin pisar lo que ya trae.</summary>
    public static void Aplicar(HttpRequestMessage request, HttpContext? contexto)
    {
        if (contexto is null || (request.Options.TryGetValue(SinSesion, out var sinSesion) && sinSesion))
        {
            return;
        }

        if (request.Headers.Authorization is null)
        {
            var jwt = Leer(contexto, CookieJwt);
            if (!string.IsNullOrWhiteSpace(jwt))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            }
        }

        if (!request.Headers.Contains(EncabezadoSesion))
        {
            var invitado = Leer(contexto, CookieInvitado);
            if (!string.IsNullOrWhiteSpace(invitado))
            {
                request.Headers.TryAddWithoutValidation(EncabezadoSesion, invitado);
            }
        }
    }

    private static string? Leer(HttpContext contexto, string clave) =>
        contexto.Request.Cookies[clave] ?? contexto.Items[clave] as string;
}
