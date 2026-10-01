using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class CarritoController : Controller
{
    private readonly GatewayClient _gateway;

    public CarritoController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    private void EnsureGuestSession()
    {
        if (Request.Cookies["sgo_jwt"] is null && Request.Cookies["sgo_guest_id"] is null)
        {
            var guestId = Guid.NewGuid().ToString("N");
            Response.Cookies.Append("sgo_guest_id", guestId, new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
            HttpContext.Items["sgo_guest_id"] = guestId;
        }
    }

    private bool EsAdminOStaff()
    {
        var jwt = Request.Cookies["sgo_jwt"];
        var userRole = Request.Cookies["sgo_user_role"] ?? "CLIENTE";
        var userEmail = Request.Cookies["sgo_user_email"];
        return PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (EsAdminOStaff())
        {
            TempData["Info"] = "Las cuentas de administración gestionan la plataforma y despachos. Para comprar en la tienda, utiliza una cuenta de cliente.";
            return RedirectToAction("Index", "Despacho");
        }

        EnsureGuestSession();
        var carrito = await _gateway.ObtenerCarritoAsync();
        return View(carrito);
    }

    [HttpPost]
    public async Task<IActionResult> Agregar(string sku, string descripcion, int cantidad, decimal precioUnitario, string? returnUrl = null)
    {
        var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest" ||
                     Request.Headers.Accept.ToString().Contains("application/json");

        if (EsAdminOStaff())
        {
            if (isAjax)
            {
                return Json(new { exito = false, mensaje = "Las cuentas de administración gestionan la plataforma. Utiliza una cuenta de cliente para comprar." });
            }
            return RedirectToAction("Index", "Despacho");
        }

        EnsureGuestSession();
        var ok = await _gateway.AgregarItemCarritoAsync(new AgregarItemModel(sku, descripcion, cantidad, precioUnitario));

        var carrito = await _gateway.ObtenerCarritoAsync();
        var totalItems = carrito?.Items?.Sum(i => i.Cantidad) ?? cantidad;

        if (isAjax)
        {
            return Json(new
            {
                exito = ok,
                sku,
                descripcion,
                cantidad,
                totalItems,
                mensaje = ok ? $"Se agregó '{descripcion}' al carrito." : "No se pudo agregar el producto."
            });
        }

        TempData["Exito"] = $"Se agregó '{descripcion}' al carrito.";

        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Catalogo");
    }

    [HttpPost]
    public async Task<IActionResult> ActualizarCantidad(string sku, int cantidad)
    {
        EnsureGuestSession();
        if (cantidad <= 0)
        {
            await _gateway.EliminarItemCarritoAsync(sku);
        }
        else
        {
            await _gateway.ActualizarCantidadItemCarritoAsync(sku, cantidad);
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Incrementar(string sku, int cantidadActual)
    {
        EnsureGuestSession();
        await _gateway.ActualizarCantidadItemCarritoAsync(sku, Math.Max(1, cantidadActual) + 1);
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Decrementar(string sku, int cantidadActual)
    {
        EnsureGuestSession();
        if (cantidadActual <= 1)
        {
            await _gateway.EliminarItemCarritoAsync(sku);
        }
        else
        {
            await _gateway.ActualizarCantidadItemCarritoAsync(sku, cantidadActual - 1);
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Eliminar(string sku)
    {
        await _gateway.EliminarItemCarritoAsync(sku);
        return RedirectToAction("Index");
    }
}
