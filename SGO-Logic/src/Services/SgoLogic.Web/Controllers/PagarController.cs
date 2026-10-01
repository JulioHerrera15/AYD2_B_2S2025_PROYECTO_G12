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

    [HttpPost("/Pagar/Reintentar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcesarReintento([FromForm] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ViewBag.Error = "Token de reintento no válido.";
            return View("Reintentar", null);
        }

        var (exito, pago, error) = await _gateway.EjecutarReintentoPagoAsync(token, ct);
        if (!exito || pago is null)
        {
            ViewBag.Error = error ?? "Ocurrió un error al procesar el reintento de pago.";
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
