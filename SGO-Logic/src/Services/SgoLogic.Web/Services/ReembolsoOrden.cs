namespace SgoLogic.Web.Services;

/// <summary>
/// Como se muestra al cliente el reembolso a su tarjeta de una orden cancelada. Codigo es
/// el estado de Ordenes (PENDIENTE, APLICADO o FALLIDO); solo decide el sondeo, nunca se muestra.
/// </summary>
public sealed record ReembolsoOrdenVista(string Titulo, string Estado, string Detalle, string Clase, string Icono, string Codigo = "")
{
    /// <summary>El reembolso sigue en proceso: la vista consulta su estado hasta que se resuelva.</summary>
    public bool Pendiente => Codigo == "PENDIENTE";
}

/// <summary>Modelo del parcial _ReembolsoOrden: banner de reembolso de una orden.</summary>
public sealed record ReembolsoBannerModel(string OrdenId, ReembolsoOrdenVista Vista, bool Compacto = false);

/// <summary>
/// Textos del reembolso por cancelacion (OrdenDto.EstadoReembolso) en lenguaje llano,
/// sin codigos. Null si la orden no tiene reembolso que mostrar.
/// </summary>
public static class ReembolsoOrden
{
    public static ReembolsoOrdenVista? Vista(OrdenDetalleModel? orden)
    {
        if (orden is null || !orden.TieneReembolso)
        {
            return null;
        }

        var titulo = $"Reembolso de Q {orden.Total:N2} a tu tarjeta";
        return orden.EstadoReembolso switch
        {
            "APLICADO" => new ReembolsoOrdenVista(
                titulo,
                "Aplicado",
                "Ya devolvimos el dinero a tu tarjeta. Según tu banco, puede tardar unos días en verse en tu estado de cuenta.",
                "bg-emerald-50 border-emerald-200 text-emerald-900",
                "check_circle",
                "APLICADO"),
            "FALLIDO" => new ReembolsoOrdenVista(
                titulo,
                "No se pudo completar",
                "No pudimos completar el reembolso. Comunícate con atención al cliente para resolverlo.",
                "bg-rose-50 border-rose-200 text-rose-900",
                "error",
                "FALLIDO"),
            _ => new ReembolsoOrdenVista(
                titulo,
                "En proceso",
                "Estamos devolviendo el dinero a tu tarjeta. Esta página se actualiza sola cuando quede aplicado.",
                "bg-amber-50 border-amber-200 text-amber-900",
                "hourglass_top",
                "PENDIENTE")
        };
    }
}
