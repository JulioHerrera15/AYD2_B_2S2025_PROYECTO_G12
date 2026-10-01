using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

/// <summary>
/// Informe de ventas conciliado y auditable: lotes enviados al ERP, totales
/// SGO vs ERP, diferencias, ordenes que componen cada lote y su bitacora.
/// </summary>
public class InformeVentasController : Controller
{
    private readonly GatewayClient _gateway;

    public InformeVentasController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta)
    {
        if (Denegado() is { } denegado) return denegado;

        ViewBag.Desde = desde;
        ViewBag.Hasta = hasta;
        return View(await _gateway.ObtenerInformeVentasAsync(desde, hasta));
    }

    [HttpPost]
    public async Task<IActionResult> Conciliar()
    {
        if (Denegado() is { } denegado) return denegado;

        var (exito, error) = await _gateway.EjecutarConciliacionAsync();
        if (exito) TempData["Exito"] = "Consolidación y conciliación con el ERP ejecutadas.";
        else TempData["Error"] = $"No se pudo conciliar: {error}";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Auditoria(string id)
    {
        if (Denegado() is not null) return Unauthorized();
        return Json(await _gateway.ObtenerAuditoriaAgregadoAsync(id));
    }

    [HttpGet]
    public async Task<IActionResult> Csv(DateTime? desde, DateTime? hasta)
    {
        if (Denegado() is { } denegado) return denegado;

        var informe = await _gateway.ObtenerInformeVentasAsync(desde, hasta);
        if (informe is null)
        {
            TempData["Error"] = "No se pudo obtener el informe de ventas del servicio ERP.";
            return RedirectToAction(nameof(Index));
        }

        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder("lote_id,estado_lote,fecha_lote,registros_sgo,monto_sgo,registros_erp,monto_erp,conciliada,diferencias,orden_id,canal,tienda,monto_orden,fecha_orden\n");
        foreach (var l in informe.Lotes)
        {
            var c = l.Conciliacion;
            var cabecera = string.Join(",",
                l.LoteId, l.Estado, l.FechaGeneracion.ToString("O", inv),
                l.CantidadRegistros, l.MontoTotal.ToString("0.00", inv),
                c?.RegistrosErp.ToString(inv) ?? "", c?.MontoErp.ToString("0.00", inv) ?? "",
                c is null ? "SIN_CONCILIAR" : c.Conciliada ? "SI" : "NO",
                Escapar(c?.Diferencias));
            foreach (var r in l.Registros)
            {
                csv.Append(cabecera).Append(',')
                   .AppendJoin(',', Escapar(r.OrdenId), r.Canal, Escapar(r.Tienda), r.Monto.ToString("0.00", inv), r.Fecha.ToString("O", inv))
                   .Append('\n');
            }
        }

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv", $"informe-ventas-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private static string Escapar(string? valor) =>
        string.IsNullOrEmpty(valor) ? "" : $"\"{valor.Replace("\"", "\"\"")}\"";

    private IActionResult? Denegado()
    {
        var jwt = Request.Cookies["sgo_jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return RedirectToAction("Login", "Auth", new { returnUrl = "/InformeVentas" });
        }

        if (!PermisosHelper.EsPersonalAutorizado(Request.Cookies["sgo_user_role"], Request.Cookies["sgo_user_email"], jwt))
        {
            TempData["Error"] = "Acceso denegado. El informe de ventas es exclusivo para personal de MegaStore.";
            return RedirectToAction("Index", "Home");
        }

        return null;
    }
}
