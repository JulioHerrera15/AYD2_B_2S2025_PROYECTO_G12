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

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        EnsureGuestSession();
        var carrito = await _gateway.ObtenerCarritoAsync();
        return View(carrito);
    }

    [HttpPost]
    public async Task<IActionResult> Agregar(string sku, string descripcion, int cantidad, decimal precioUnitario)
    {
        EnsureGuestSession();
        await _gateway.AgregarItemCarritoAsync(new AgregarItemModel(sku, descripcion, cantidad, precioUnitario));
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Eliminar(string sku)
    {
        await _gateway.EliminarItemCarritoAsync(sku);
        return RedirectToAction("Index");
    }
}
