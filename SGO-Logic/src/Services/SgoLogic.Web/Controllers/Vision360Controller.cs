using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public class Vision360Controller : Controller
{
    private readonly GatewayClient _gateway;

    public Vision360Controller(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? email, string? clienteId)
    {
        var targetCliente = !string.IsNullOrWhiteSpace(clienteId)
            ? clienteId.Trim()
            : (!string.IsNullOrWhiteSpace(email)
                ? email.Trim()
                : (Request.Cookies["sgo_user_email"] ?? "juan.perez@megastore.com"));

        ViewBag.TargetCliente = targetCliente;

        // Consultar historial de órdenes en ConsultaEstado
        var historial = await _gateway.ObtenerHistorialClienteAsync(targetCliente);

        // Consultar carrito activo en Redis
        var carrito = await _gateway.ObtenerCarritoPorIdAsync(targetCliente);

        ViewBag.Historial = historial;
        ViewBag.Carrito = carrito;

        return View();
    }
}
