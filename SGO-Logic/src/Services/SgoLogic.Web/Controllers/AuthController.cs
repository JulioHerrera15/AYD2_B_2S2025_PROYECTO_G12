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
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Email = email;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(contrasena))
        {
            ViewBag.Error = "Por favor ingresa tu correo electrónico y contraseña.";
            return View();
        }

        var (exito, result, error) = await _gateway.LoginAsync(new LoginModel(email, contrasena));
        if (!exito || result is null)
        {
            if (error != null && (error.Contains("no verificada", StringComparison.OrdinalIgnoreCase) || error.Contains("verificar", StringComparison.OrdinalIgnoreCase)))
            {
                TempData["EmailVerificar"] = email;
                TempData["Error"] = "Tu cuenta aún no está verificada. Por favor ingresa el código de 6 dígitos enviado a tu correo.";
                return RedirectToAction("Verificar", new { email });
            }

            string mensajeAmigable;
            if (string.IsNullOrWhiteSpace(error) ||
                error.Contains("Credenciales incorrectas", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("No autorizado", StringComparison.OrdinalIgnoreCase))
            {
                mensajeAmigable = "Credenciales incorrectas. Por favor verifica tu correo y contraseña.";
            }
            else
            {
                mensajeAmigable = error;
            }

            ViewBag.Error = mensajeAmigable;
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

        var userRole = !string.IsNullOrWhiteSpace(result.Rol)
            ? result.Rol
            : PermisosHelper.ObtenerRolDesdeJwt(result.Token);

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
            string errorAmigable = error ?? "Error al registrar la cuenta.";
            if (errorAmigable.Contains("Conflicto", StringComparison.OrdinalIgnoreCase) ||
                errorAmigable.Contains("ya existe", StringComparison.OrdinalIgnoreCase) ||
                errorAmigable.Contains("registrado", StringComparison.OrdinalIgnoreCase))
            {
                errorAmigable = "Ya existe una cuenta registrada con este correo electrónico.";
            }
            ViewBag.Error = errorAmigable;
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
            string errorAmigable = error ?? "Código de verificación incorrecto o expirado.";
            if (errorAmigable.Contains("incorrecto", StringComparison.OrdinalIgnoreCase) ||
                errorAmigable.Contains("expirado", StringComparison.OrdinalIgnoreCase) ||
                errorAmigable.Contains("inválido", StringComparison.OrdinalIgnoreCase) ||
                errorAmigable.Contains("invalido", StringComparison.OrdinalIgnoreCase))
            {
                errorAmigable = "El código de verificación es incorrecto o ha expirado. Verifica el código de 6 dígitos recibido.";
            }
            ViewBag.Error = errorAmigable;
            return View();
        }

        TempData["Exito"] = "Cuenta verificada exitosamente. Ya puedes iniciar sesión.";
        return RedirectToAction("Login");
    }

    [HttpPost]
    public async Task<IActionResult> ReenviarCodigo(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Por favor ingresa tu correo electrónico.";
            return RedirectToAction("Verificar");
        }

        var (exito, error) = await _gateway.ReenviarCodigoAsync(email.Trim());
        if (!exito)
        {
            TempData["Error"] = error ?? "No se pudo reenviar el código. Intenta de nuevo.";
        }
        else
        {
            TempData["Exito"] = "Te hemos enviado un nuevo código de verificación a tu correo.";
        }

        return RedirectToAction("Verificar", new { email = email.Trim() });
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
