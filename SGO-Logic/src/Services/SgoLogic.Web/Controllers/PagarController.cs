using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class PagarController : Controller
{
    private readonly GatewayClient _gateway;
    private readonly ILogger<PagarController> _logger;

    public PagarController(GatewayClient gateway, ILogger<PagarController> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    [HttpGet("/Pagar/Reintentar")]
    public async Task<IActionResult> Reintentar([FromQuery] string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ViewBag.Error = "No se proporcionó ningún token de reintento de pago.";
            return View("Reintentar", null);
        }

        var (exito, info, error) = await _gateway.ObtenerReintentoPagoAsync(token, ct);
        if (!exito || info is null)
        {
            ViewBag.Error = error ?? "No fue posible localizar el enlace de reintento solicitado.";
            ViewBag.Token = token;
            return View("Reintentar", null);
        }

        ViewBag.Token = token;
        return View("Reintentar", info);
    }

    /// <summary>
    /// Ejecuta el reintento. Si el token de la tarjeta sigue vigente se cobra sin pedir
    /// nada. Si vencio, la pagina pide de nuevo los datos: solo el CVV si era una
    /// tarjeta guardada (Pagos emite un token nuevo desde ella) o la tarjeta completa,
    /// que se tokeniza en Pagos antes de reintentar. Los datos no se registran.
    /// </summary>
    [HttpPost("/Pagar/Reintentar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcesarReintento(
        [FromForm] string token,
        [FromForm] string? numero,
        [FromForm] string? vencimiento,
        [FromForm] string? cvv,
        [FromForm] string? titular,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ViewBag.Error = "Token de reintento no válido.";
            return View("Reintentar", null);
        }

        string? tokenMedioPago = null;
        string? cvvGuardada = null;

        if (!string.IsNullOrWhiteSpace(numero))
        {
            var (mes, anio) = ParsearVencimiento(vencimiento);
            var tokenizacion = await _gateway.TokenizarTarjetaAsync(
                new SolicitudTokenPagoModel(numero, mes, anio, cvv, titular, Guardar: false), ct);

            if (tokenizacion.Token is null)
            {
                return await VolverConDatosAsync(token, tokenizacion.Mensaje, tokenizacion.Errores, ct);
            }

            tokenMedioPago = tokenizacion.Token.Token;
        }
        else if (!string.IsNullOrWhiteSpace(cvv))
        {
            cvvGuardada = cvv;
        }

        var resultado = await _gateway.EjecutarReintentoPagoAsync(token, tokenMedioPago, cvvGuardada, ct);
        if (!resultado.Exito && (resultado.DatosRequeridos is not null || resultado.Errores is not null))
        {
            return await VolverConDatosAsync(token, resultado.Error, resultado.Errores, ct);
        }

        var pago = resultado.Pago;
        if (!resultado.Exito || pago is null)
        {
            ViewBag.Error = resultado.Error ?? "Ocurrió un error al procesar el reintento de pago.";
            var (_, infoRecargada, _) = await _gateway.ObtenerReintentoPagoAsync(token, ct);
            ViewBag.Token = token;
            return View("Reintentar", infoRecargada);
        }

        if (string.Equals(pago.Estado, "APLICADO", StringComparison.OrdinalIgnoreCase))
        {
            TempData["PagoExitoso"] = "true";
            TempData["OrdenId"] = pago.OrdenId;
            TempData["Monto"] = (pago.Monto ?? 0m).ToString("N2");
            TempData["Moneda"] = pago.Moneda ?? "GTQ";
            return RedirectToAction("ReintentoExitoso", new { ordenId = pago.OrdenId });
        }

        if (string.Equals(pago.Estado, "RECHAZADO", StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.Error = "El pago fue rechazado por la pasarela de pagos. Por favor verifica tus fondos o medio de pago registrado.";
            var (_, infoRechazada, _) = await _gateway.ObtenerReintentoPagoAsync(token, ct);
            ViewBag.Token = token;
            return View("Reintentar", infoRechazada);
        }

        var ultimoIntento = pago.Intentos?.OrderByDescending(i => i.IntentoNumero).FirstOrDefault();
        if (ultimoIntento?.CodigoRespuesta == "CIRCUITO_ABIERTO")
        {
            ViewBag.Advertencia = "El disyuntor de protección (Circuit Breaker) hacia PayKlik se encuentra abierto temporalmente para evitar saturación tras detectar la caída del servicio. Se restablecerá automáticamente en unos 15 segundos. Por favor espera unos momentos antes de volver a presionar el botón de reintento.";
        }
        else if (ultimoIntento?.CodigoRespuesta == "RED")
        {
            ViewBag.Advertencia = "El servicio de pagos no respondió (error de conexión o contenedor iniciándose). Si acabas de levantarlo en Docker, dale unos segundos a la red y vuelve a intentar.";
        }
        else
        {
            ViewBag.Advertencia = "El intento de cobro fue enviado, pero el procesador de pagos (PayKlik) reportó timeout o indisponibilidad transitoria. Puedes volver a intentar en este momento con el botón inferior o usar el nuevo enlace enviado a tu correo.";
        }

        var (_, infoPendiente, _) = await _gateway.ObtenerReintentoPagoAsync(token, ct);
        ViewBag.Token = token;
        return View("Reintentar", infoPendiente);
    }

    /// <summary>Vuelve a la pagina con el formulario de datos y los errores por campo.</summary>
    private async Task<IActionResult> VolverConDatosAsync(
        string token,
        string? mensaje,
        Dictionary<string, string>? errores,
        CancellationToken ct)
    {
        var (_, info, _) = await _gateway.ObtenerReintentoPagoAsync(token, ct);
        ViewBag.Token = token;
        ViewBag.Advertencia = mensaje ?? "Revisa los datos de tu tarjeta.";
        ViewBag.ErroresTarjeta = errores ?? new Dictionary<string, string>();
        return View("Reintentar", info);
    }

    /// <summary>"MM/AA" o "MM/AAAA" a mes y anio; null en lo que no se pueda leer (lo valida Pagos).</summary>
    private static (int? Mes, int? Anio) ParsearVencimiento(string? vencimiento)
    {
        var partes = (vencimiento ?? string.Empty).Split('/', StringSplitOptions.TrimEntries);
        int? mes = partes.Length > 0 && int.TryParse(partes[0], out var m) ? m : null;
        int? anio = partes.Length > 1 && int.TryParse(partes[1], out var a) ? a : null;
        return (mes, anio);
    }

    [HttpGet("/Pagar/Exito")]
    public IActionResult ReintentoExitoso([FromQuery] string ordenId)
    {
        ViewBag.OrdenId = ordenId;
        return View("ReintentoExitoso");
    }

    [HttpGet("/api/pagos/reintentos/{token}")]
    public IActionResult RedirigirApi(string token)
    {
        return RedirectToAction("Reintentar", new { token });
    }
}
