using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class AuthController : Controller
{
    private readonly GatewayClient _gateway;

    public AuthController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string email, string contrasena, string? returnUrl = null)
    {
        var (exito, result, error) = await _gateway.LoginAsync(new LoginModel(email, contrasena));
        if (!exito || result is null)
        {
            if (error != null && (error.Contains("no verificada", StringComparison.OrdinalIgnoreCase) || error.Contains("verificar", StringComparison.OrdinalIgnoreCase)))
            {
                TempData["EmailVerificar"] = email;
                TempData["Error"] = "Tu cuenta aun no esta verificada. Por favor ingresa el codigo de 6 digitos.";
                return RedirectToAction("Verificar", new { email });
            }

            ViewBag.Error = error ?? "Credenciales invalidas o correo no verificado.";
            return View();
        }

        // Guardar JWT en cookie HTTP-only
        Response.Cookies.Append("sgo_jwt", result.Token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = result.ExpiraEn
        });

        Response.Cookies.Append("sgo_user_email", result.Email, new CookieOptions
        {
            SameSite = SameSiteMode.Lax,
            Expires = result.ExpiraEn
        });

        Response.Cookies.Append("sgo_user_name", result.NombreCompleto, new CookieOptions
        {
            SameSite = SameSiteMode.Lax,
            Expires = result.ExpiraEn
        });

        var userRole = email.Contains("admin", StringComparison.OrdinalIgnoreCase) ? "ADMIN"
            : (email.EndsWith("@megastore.com", StringComparison.OrdinalIgnoreCase) ? "OPERADOR" : "CLIENTE");

        Response.Cookies.Append("sgo_user_role", userRole, new CookieOptions
        {
            SameSite = SameSiteMode.Lax,
            Expires = result.ExpiraEn
        });

        // Si habia un carrito de invitado, asociarlo
        var guestId = Request.Cookies["sgo_guest_id"];
        if (!string.IsNullOrWhiteSpace(guestId))
        {
            await _gateway.AsociarCarritoAsync(guestId, result.Token);
            Response.Cookies.Delete("sgo_guest_id");
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Registro()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Registro(
        string? nombre,
        string? apellido,
        string? telefono,
        string email,
        string contrasena,
        string? confirmarContrasena,
        string? nombreCompleto)
    {
        ViewBag.Nombre = nombre;
        ViewBag.Apellido = apellido;
        ViewBag.Telefono = telefono;
        ViewBag.Email = email;

        if (!string.IsNullOrWhiteSpace(confirmarContrasena) && !string.Equals(contrasena, confirmarContrasena))
        {
            ViewBag.Error = "Las contraseñas no coinciden.";
            return View();
        }

        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < 8)
        {
            ViewBag.Error = "La contraseña debe cumplir la política mínima de seguridad (al menos 8 caracteres).";
            return View();
        }

        var nombreFinal = !string.IsNullOrWhiteSpace(nombre) && !string.IsNullOrWhiteSpace(apellido)
            ? $"{nombre} {apellido}".Trim()
            : (!string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : (nombre ?? "Usuario"));

        var (exito, error) = await _gateway.RegistrarAsync(new RegistroModel(email, contrasena, nombreFinal));
        if (!exito)
        {
            ViewBag.Error = error ?? "Error al registrar la cuenta.";
            return View();
        }

        TempData["EmailVerificar"] = email;
        return RedirectToAction("Verificar", new { email });
    }

    [HttpGet]
    public IActionResult Verificar(string? email)
    {
        ViewBag.Email = email ?? TempData["EmailVerificar"] as string;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Verificar(string email, string codigo)
    {
        var (exito, error) = await _gateway.VerificarAsync(new VerificarModel(email, codigo));
        if (!exito)
        {
            ViewBag.Email = email;
            ViewBag.Error = error ?? "Codigo de verificacion incorrecto o expirado.";
            return View();
        }

        TempData["Exito"] = "Cuenta verificada exitosamente. Ya puedes iniciar sesion.";
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("sgo_jwt");
        Response.Cookies.Delete("sgo_user_email");
        Response.Cookies.Delete("sgo_user_name");
        Response.Cookies.Delete("sgo_user_role");
        return RedirectToAction("Index", "Home");
    }
}
