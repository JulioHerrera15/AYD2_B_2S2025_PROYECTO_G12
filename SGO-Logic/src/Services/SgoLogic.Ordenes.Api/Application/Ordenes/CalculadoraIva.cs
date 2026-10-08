namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>Seccion Fiscal de la configuracion.</summary>
public sealed class FiscalOptions
{
    public const string Seccion = "Fiscal";

    /// <summary>Tasa del IVA incluida en los precios del catalogo (0.12 en Guatemala).</summary>
    public decimal TasaIva { get; set; } = 0.12m;
}

/// <summary>
/// IVA incluido en un monto. Los precios del catalogo ya traen IVA: el total de la
/// orden es la suma de las lineas y el IVA se desglosa de ese total, nunca se suma.
/// </summary>
public static class CalculadoraIva
{
    /// <summary>Total - Total / (1 + tasa), redondeado a 2 decimales.</summary>
    public static decimal IvaIncluido(decimal total, decimal tasa)
    {
        if (total <= 0m || tasa <= 0m)
        {
            return 0m;
        }

        return Math.Round(total - total / (1m + tasa), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Reparte el IVA de la orden entre sus lineas en proporcion a su importe. La
    /// ultima linea absorbe la diferencia de redondeo, asi la suma por linea es
    /// exactamente el IVA de la orden.
    /// </summary>
    public static IReadOnlyList<decimal> IvaPorLinea(IReadOnlyList<decimal> importesLineas, decimal ivaTotal)
    {
        var resultado = new decimal[importesLineas.Count];
        var total = importesLineas.Sum();
        var acumulado = 0m;
        for (var i = 0; i < importesLineas.Count; i++)
        {
            resultado[i] = i == importesLineas.Count - 1 || total <= 0m
                ? ivaTotal - acumulado
                : Math.Round(ivaTotal * importesLineas[i] / total, 2, MidpointRounding.AwayFromZero);
            acumulado += resultado[i];
        }

        return resultado;
    }
}
