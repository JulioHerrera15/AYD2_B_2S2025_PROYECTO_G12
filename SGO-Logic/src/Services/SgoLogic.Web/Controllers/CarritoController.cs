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

    /// <summary>Sucursal elegida en el catalogo: Inventario intenta apartar ahi primero.</summary>
    private string? SucursalPreferida()
    {
        var sucursal = Request.Cookies["sgo_sucursal"];
        return string.IsNullOrWhiteSpace(sucursal) ? null : sucursal.Trim();
    }

    /// <summary>
    /// Texto para el cliente cuando Inventario no aparto el stock (409) o no
    /// respondio (503). Si Inventario sugirio una tienda con existencias, se nombra.
    /// </summary>
    private static string MensajeRechazo(ResultadoCarritoModel resultado)
    {
        var mensaje = resultado.Mensaje ?? "No se pudo actualizar el carrito.";
        if (resultado.SucursalCercana is { } cercana)
        {
            var distancia = cercana.DistanciaKm is null ? string.Empty : $" a {cercana.DistanciaKm:0.#} km";
            mensaje += $" Hay {cercana.Disponible} disponible(s) en {cercana.Nombre}{distancia}.";
        }

        return mensaje;
    }

    private void InformarSiFallo(ResultadoCarritoModel resultado)
    {
        if (!resultado.Exito)
        {
            TempData["Error"] = MensajeRechazo(resultado);
        }
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
        var resultado = await _gateway.AgregarItemCarritoAsync(
            new AgregarItemModel(sku, descripcion, cantidad, precioUnitario, SucursalPreferida()));

        if (!resultado.Exito)
        {
            var mensajeRechazo = MensajeRechazo(resultado);

            if (isAjax)
            {
                Response.StatusCode = resultado.Status is StatusCodes.Status409Conflict or StatusCodes.Status503ServiceUnavailable
                    ? resultado.Status
                    : StatusCodes.Status400BadRequest;

                return Json(new
                {
                    exito = false,
                    sku,
                    descripcion,
                    mensaje = mensajeRechazo,
                    sucursalCercana = resultado.SucursalCercana
                });
            }

            TempData["Error"] = mensajeRechazo;
            return !string.IsNullOrWhiteSpace(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Catalogo");
        }

        var carrito = await _gateway.ObtenerCarritoAsync();
        var totalItems = carrito?.Items?.Sum(i => i.Cantidad) ?? cantidad;
        var item = carrito?.Items?.FirstOrDefault(i => i.Sku == sku);

        if (isAjax)
        {
            return Json(new
            {
                exito = true,
                sku,
                descripcion,
                cantidad,
                totalItems,
                ubicacionId = item?.UbicacionId,
                nombreUbicacion = item?.NombreUbicacion,
                enOtraSucursal = item?.EnOtraSucursal ?? false,
                mensaje = $"Se agregó '{descripcion}' al carrito."
            });
        }

        TempData["Exito"] = item?.EnOtraSucursal == true && !string.IsNullOrWhiteSpace(item.NombreUbicacion)
            ? $"Se agregó '{descripcion}' al carrito, apartado en {item.NombreUbicacion} porque tu sucursal no tiene suficiente."
            : $"Se agregó '{descripcion}' al carrito.";

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
        var resultado = cantidad <= 0
            ? await _gateway.EliminarItemCarritoAsync(sku)
            : await _gateway.ActualizarCantidadItemCarritoAsync(sku, cantidad, SucursalPreferida());

        InformarSiFallo(resultado);
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Incrementar(string sku, int cantidadActual)
    {
        EnsureGuestSession();
        var resultado = await _gateway.ActualizarCantidadItemCarritoAsync(sku, Math.Max(1, cantidadActual) + 1, SucursalPreferida());
        InformarSiFallo(resultado);
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Decrementar(string sku, int cantidadActual)
    {
        EnsureGuestSession();
        var resultado = cantidadActual <= 1
            ? await _gateway.EliminarItemCarritoAsync(sku)
            : await _gateway.ActualizarCantidadItemCarritoAsync(sku, cantidadActual - 1, SucursalPreferida());

        InformarSiFallo(resultado);
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> Eliminar(string sku)
    {
        var resultado = await _gateway.EliminarItemCarritoAsync(sku);
        InformarSiFallo(resultado);
        return RedirectToAction("Index");
    }

    /// <summary>
    /// La cuenta regresiva llego a cero: pide a Auth que renueve la retencion de
    /// stock de todo el carrito. Los productos que ya no tengan stock quedan
    /// marcados como reserva vencida.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RenovarReserva()
    {
        EnsureGuestSession();
        var (resultado, carrito) = await _gateway.RenovarReservaCarritoAsync();

        if (!resultado.Exito)
        {
            TempData["Error"] = MensajeRechazo(resultado);
        }
        else if (carrito?.TieneReservasVencidas == true)
        {
            TempData["Error"] = "Algunos productos ya no tienen existencias para apartarse de nuevo. Quítalos o cambia la cantidad para continuar.";
        }
        else
        {
            TempData["Exito"] = "Renovamos el apartado de tus productos.";
        }

        return RedirectToAction("Index");
    }
}
