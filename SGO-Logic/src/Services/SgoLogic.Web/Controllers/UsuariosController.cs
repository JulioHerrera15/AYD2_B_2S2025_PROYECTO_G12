using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class UsuariosController : Controller
{
    private readonly GatewayClient _gateway;

    public UsuariosController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Acceso restringido. Debes iniciar sesion con credenciales de operador o administrador.";
            return RedirectToAction("Login", "Auth", new { returnUrl = "/Usuarios" });
        }

        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Error"] = "Acceso denegado. La administracion de roles es exclusiva para operadores y administradores.";
            return RedirectToAction("Index", "Home");
        }

        var usuarios = await _gateway.ListarUsuariosAsync();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var filtro = q.Trim().ToLowerInvariant();
            usuarios = usuarios.Where(u =>
                (u.Email != null && u.Email.ToLowerInvariant().Contains(filtro)) ||
                (u.NombreCompleto != null && u.NombreCompleto.ToLowerInvariant().Contains(filtro)) ||
                (u.Rol != null && u.Rol.ToLowerInvariant().Contains(filtro))
            ).ToList();
        }

        ViewBag.SearchQuery = q;
        ViewBag.CurrentUserEmail = userEmail;
        ViewBag.CurrentUserRole = userRole;

        return View(usuarios);
    }

    [HttpPost]
    public async Task<IActionResult> AsignarRol(string email, string nuevoRol)
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            TempData["Error"] = "Sesion no valida.";
            return RedirectToAction("Login", "Auth");
        }

        var userRole = Request.Cookies["sgo_user_role"];
        var userEmail = Request.Cookies["sgo_user_email"];
        if (!PermisosHelper.EsPersonalAutorizado(userRole, userEmail, jwt))
        {
            TempData["Error"] = "No tienes permisos para modificar roles de usuario.";
            return RedirectToAction("Index");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(nuevoRol))
        {
            TempData["Error"] = "El correo y el nuevo rol son requeridos.";
            return RedirectToAction("Index");
        }

        var rolLimpio = nuevoRol.Trim().ToUpperInvariant();
        if (rolLimpio != "CLIENTE" && rolLimpio != "OPERADOR" && rolLimpio != "ADMIN")
        {
            TempData["Error"] = "El rol especificado es invalido. Solo se admiten CLIENTE, OPERADOR y ADMIN.";
            return RedirectToAction("Index");
        }

        var (exito, error) = await _gateway.AsignarRolAsync(email.Trim(), rolLimpio);
        if (exito)
        {
            TempData["Exito"] = $"Rol del usuario {email} actualizado correctamente a {rolLimpio}.";
        }
        else
        {
            TempData["Error"] = $"Error al asignar rol: {error ?? "Ocurrio un problema en el servidor de autenticacion"}";
        }

        return RedirectToAction("Index");
    }
}
