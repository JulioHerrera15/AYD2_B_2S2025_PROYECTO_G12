namespace SgoLogic.IntegracionErp.Worker.Infrastructure;

public sealed class ErpOptions
{
    public const string Seccion = "Erp";
    public string BaseUrl { get; set; } = "http://localhost:4000";
    public string Formato { get; set; } = "CSV";
    public int TiempoEsperaMs { get; set; } = 5000;
    public int MaxReintentos { get; set; } = 3;
    public int MaxIntentosLote { get; set; } = 5;
    public int IntervaloConsolidacionMinutos { get; set; } = 30;
    public int IntervaloHistorialMinutos { get; set; } = 30;
    public bool EjecutarAlArrancar { get; set; } = true;
    public bool EjecutarHistorialAlArrancar { get; set; } = true;
    public int IntervaloConciliacionHoras { get; set; } = 24;
}
