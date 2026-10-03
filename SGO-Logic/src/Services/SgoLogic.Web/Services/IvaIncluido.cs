namespace SgoLogic.Web.Services;

/// <summary>
/// IVA incluido en un total, solo para mostrarlo. Mismo calculo que hace Ordenes
/// (Fiscal:TasaIva): los precios ya traen IVA y el total es la suma de las lineas.
/// </summary>
public static class IvaIncluido
{
    public const decimal Tasa = 0.12m;

    public static decimal De(decimal total) =>
        total <= 0m ? 0m : Math.Round(total - total / (1m + Tasa), 2, MidpointRounding.AwayFromZero);
}
