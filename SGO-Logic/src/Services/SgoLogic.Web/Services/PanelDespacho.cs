using SgoLogic.Web.Controllers;

namespace SgoLogic.Web.Services;

/// <summary>
/// Arma el modelo del panel de despacho a partir de la lista completa que devuelve
/// el servicio. Es puro (sin HTTP ni cookies) para poder probarlo aparte del
/// controlador.
///
/// Recibe siempre la lista de todas las ubicaciones y filtra la sede en memoria: asi
/// el selector de sedes se arma con los origenes reales aunque se este viendo una
/// sola, sin una segunda llamada.
/// </summary>
public static class PanelDespacho
{
    public const string TodasLasUbicaciones = "TODAS";

    /// <summary>Estado de Despacho con el que se representa un despacho cancelado.</summary>
    private const string EstadoAnulado = "ANULADA";

    /// <summary>
    /// Estados desde los que el dominio de Despacho (TransicionesEntrega) admite el
    /// paso a ruta: EN_TRANSITO para domicilio y LISTA_PARA_RECOGER para recogida.
    /// Un despacho cancelado nunca muestra el boton.
    /// </summary>
    private static readonly HashSet<string> OrigenesHaciaEnTransito =
        new(StringComparer.OrdinalIgnoreCase) { "EN_PREPARACION", "GUIA_ASIGNADA", "GUIA_PENDIENTE", "INCIDENCIA" };

    private static readonly HashSet<string> OrigenesHaciaListaParaRecoger =
        new(StringComparer.OrdinalIgnoreCase) { "EN_PREPARACION", "RECOGIDA_TIENDA", "INCIDENCIA" };

    /// <param name="despachos">Despachos de todas las ubicaciones; null si el servicio fallo.</param>
    /// <param name="filtro">Metodo de entrega: TODOS, DOMICILIO o RECOGIDA_TIENDA.</param>
    /// <param name="ubicacionId">Sede elegida, o <see cref="TodasLasUbicaciones" />.</param>
    public static DespachoViewModel Construir(
        IReadOnlyList<DespachoPreparacionDto>? despachos,
        string filtro,
        string ubicacionId)
    {
        if (despachos is null)
        {
            return new DespachoViewModel(
                Filtro: filtro,
                TotalDespachos: 0,
                EnPreparacion: 0,
                EnTransito: 0,
                Entregados: 0,
                ConteoDomicilio: 0,
                ConteoTienda: 0,
                Items: new List<DespachoItemModel>(),
                ColaPreparacion: new List<DespachoPreparacionDto>(),
                UbicacionId: ubicacionId,
                Sedes: SedesDisponibles(Array.Empty<DespachoPreparacionDto>(), ubicacionId),
                Error: "No se pudo consultar el servicio de Despacho. La lista no esta disponible en este momento; intenta de nuevo en unos segundos.");
        }

        var unicos = despachos
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.DespachoId) ? x.DespachoId : x.OrdenId)
            .Select(g => g.First())
            .ToList();

        var colaReal = unicos
            .Where(x => ubicacionId == TodasLasUbicaciones ||
                        string.Equals(x.UbicacionOrigenId, ubicacionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreadoEn)
            .ToList();

        var todos = colaReal.Select(item => new DespachoItemModel(
            IdDespacho: item.DespachoId,
            OrdenId: item.OrdenId,
            Destino: Destino(item),
            Metodo: item.MetodoEntrega,
            MetodoLabel: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "Recogida Tienda" : "Domicilio",
            MetodoIcon: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "storefront" : "local_shipping",
            Transportista: item.MetodoEntrega == "RECOGIDA_TIENDA" ? "Flota Interna" : "LogiExpress",
            Guia: item.GuiaId,
            Estado: item.Estado,
            EstadoBadgeClass: MapearBadgeEstado(item.Estado),
            EstadoLabel: item.Estado == EstadoAnulado ? "CANCELADO" : item.Estado,
            PuedeEnviarARuta: PuedeEnviarARuta(item.Estado, item.MetodoEntrega)
        )).ToList();

        var filtrados = filtro switch
        {
            "DOMICILIO" => todos.Where(d => d.Metodo == "DOMICILIO").ToList(),
            "RECOGIDA_TIENDA" or "TIENDA" => todos.Where(d => d.Metodo == "RECOGIDA_TIENDA").ToList(),
            _ => todos
        };

        return new DespachoViewModel(
            Filtro: filtro,
            TotalDespachos: todos.Count,
            EnPreparacion: todos.Count(d => d.Estado == "EN_PREPARACION" || d.Estado == "GUIA_ASIGNADA"),
            EnTransito: todos.Count(d => d.Estado == "EN_TRANSITO"),
            Entregados: todos.Count(d => d.Estado == "ENTREGADA" || d.Estado == "LISTA_PARA_RECOGER"),
            ConteoDomicilio: todos.Count(d => d.Metodo == "DOMICILIO"),
            ConteoTienda: todos.Count(d => d.Metodo == "RECOGIDA_TIENDA"),
            Items: filtrados,
            ColaPreparacion: colaReal,
            UbicacionId: ubicacionId,
            Sedes: SedesDisponibles(unicos, ubicacionId));
    }

    /// <summary>
    /// Origenes distintos presentes en los despachos. La sede elegida se incluye
    /// aunque no tenga despachos, para que el selector siga mostrandola activa.
    /// </summary>
    private static List<string> SedesDisponibles(IEnumerable<DespachoPreparacionDto> despachos, string ubicacionId)
    {
        var sedes = despachos
            .Select(d => d.UbicacionOrigenId)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ubicacionId != TodasLasUbicaciones)
        {
            sedes.Add(ubicacionId);
        }

        return sedes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// En recogida el destino es la tienda que eligio el cliente; si no viene, la de
    /// origen (Despacho saca la recogida de esa misma tienda).
    /// </summary>
    private static string Destino(DespachoPreparacionDto item)
    {
        if (item.MetodoEntrega == "RECOGIDA_TIENDA")
        {
            var tienda = !string.IsNullOrWhiteSpace(item.UbicacionRecogidaId)
                ? item.UbicacionRecogidaId
                : item.UbicacionOrigenId;
            return $"Recogida en Tienda ({tienda ?? "sin asignar"})";
        }

        return $"Envío a Domicilio ({item.UbicacionOrigenId})";
    }

    private static bool PuedeEnviarARuta(string estado, string metodoEntrega) =>
        metodoEntrega == "RECOGIDA_TIENDA"
            ? OrigenesHaciaListaParaRecoger.Contains(estado)
            : OrigenesHaciaEnTransito.Contains(estado);

    private static string MapearBadgeEstado(string estado) => estado switch
    {
        EstadoAnulado => "bg-gray-100 text-gray-600 border border-gray-300",
        "ENTREGADA" or "LISTA_PARA_RECOGER" => "bg-emerald-100 text-emerald-800 border border-emerald-200",
        "EN_TRANSITO" => "bg-purple-100 text-purple-800 border border-purple-200",
        "EN_PREPARACION" => "bg-amber-100 text-amber-800 border border-amber-200",
        _ => "bg-blue-100 text-blue-800 border border-blue-200"
    };
}
