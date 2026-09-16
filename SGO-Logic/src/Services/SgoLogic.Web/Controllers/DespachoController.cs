using Microsoft.AspNetCore.Mvc;
using SgoLogic.Web.Services;

namespace SgoLogic.Web.Controllers;

public sealed record DespachoItemModel(
    string IdDespacho,
    string OrdenId,
    string Destino,
    string Metodo, // "DOMICILIO" o "RECOGIDA_TIENDA"
    string MetodoLabel,
    string MetodoIcon,
    string Transportista,
    string? Guia,
    string Estado,
    string EstadoBadgeClass);

public sealed record DespachoViewModel(
    string Filtro,
    int TotalDespachos,
    int EnPreparacion,
    int EnTransito,
    int Entregados,
    int ConteoDomicilio,
    int ConteoTienda,
    List<DespachoItemModel> Items);

public class DespachoController : Controller
{
    private readonly GatewayClient _gateway;

    public DespachoController(GatewayClient gateway)
    {
        _gateway = gateway;
    }

    [HttpGet]
    public IActionResult Index(string? filtro)
    {
        var filtroNormalizado = string.IsNullOrWhiteSpace(filtro) ? "TODOS" : filtro.Trim().ToUpperInvariant();
        ViewBag.Filtro = filtroNormalizado;

        var todos = ObtenerDespachos();

        var filtrados = filtroNormalizado switch
        {
            "DOMICILIO" => todos.Where(d => d.Metodo == "DOMICILIO").ToList(),
            "RECOGIDA_TIENDA" or "TIENDA" => todos.Where(d => d.Metodo == "RECOGIDA_TIENDA").ToList(),
            _ => todos
        };

        var model = new DespachoViewModel(
            Filtro: filtroNormalizado,
            TotalDespachos: todos.Count,
            EnPreparacion: todos.Count(d => d.Estado == "EN_PREPARACION"),
            EnTransito: todos.Count(d => d.Estado == "EN_TRANSITO"),
            Entregados: todos.Count(d => d.Estado == "ENTREGADA" || d.Estado == "LISTA_PARA_RECOGER"),
            ConteoDomicilio: todos.Count(d => d.Metodo == "DOMICILIO"),
            ConteoTienda: todos.Count(d => d.Metodo == "RECOGIDA_TIENDA"),
            Items: filtrados
        );

        return View(model);
    }

    private static List<DespachoItemModel> ObtenerDespachos() =>
    [
        new(
            "DSP-2026-0812",
            "ORD-2026-000001",
            "MegaStore Central — Zona 10",
            "RECOGIDA_TIENDA",
            "Recogida Tienda",
            "storefront",
            "Flota Interna",
            null,
            "LISTA_PARA_RECOGER",
            "bg-green-100 text-green-800 border border-green-200"
        ),
        new(
            "DSP-2026-0813",
            "ORD-2026-000002",
            "Avenida Las Américas 14-20, Zona 14",
            "DOMICILIO",
            "Domicilio",
            "local_shipping",
            "LogiExpress",
            "GUIA-LX-9942",
            "EN_TRANSITO",
            "bg-purple-100 text-purple-800 border border-purple-200"
        ),
        new(
            "DSP-2026-0814",
            "ORD-2026-000003",
            "Calzada Roosevelt 22-45, Zona 11",
            "DOMICILIO",
            "Domicilio",
            "local_shipping",
            "LogiExpress",
            "GUIA-LX-9945",
            "EN_PREPARACION",
            "bg-amber-100 text-amber-800 border border-amber-200"
        ),
        new(
            "DSP-2026-0815",
            "ORD-2026-000004",
            "MegaStore Miraflores — Zona 11",
            "RECOGIDA_TIENDA",
            "Recogida Tienda",
            "storefront",
            "Flota Interna",
            null,
            "ENTREGADA",
            "bg-green-100 text-green-800 border border-green-200"
        ),
        new(
            "DSP-2026-0816",
            "ORD-2026-000028",
            "Boulevard Los Próceres 12-50, Zona 10",
            "DOMICILIO",
            "Domicilio",
            "local_shipping",
            "LogiExpress",
            "GUIA-LX-9948",
            "EN_TRANSITO",
            "bg-purple-100 text-purple-800 border border-purple-200"
        ),
        new(
            "DSP-2026-0817",
            "ORD-2026-000025",
            "MegaStore Pradera Concepción",
            "RECOGIDA_TIENDA",
            "Recogida Tienda",
            "storefront",
            "Flota Interna",
            null,
            "LISTA_PARA_RECOGER",
            "bg-green-100 text-green-800 border border-green-200"
        )
    ];
}
