namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Parametros de la politica de credito (seccion Credito:Politica). Los valores por
/// defecto son los de la version 2026.10-v1; cambiar cualquiera de ellos es una
/// politica nueva y deberia ir con otra VersionPolitica.
/// </summary>
public sealed class PoliticaCreditoOptions
{
    public const string Seccion = "Credito:Politica";

    public string VersionPolitica { get; set; } = "2026.10-v1";
    public string Moneda { get; set; } = "GTQ";
    public int PlazoDias { get; set; } = 30;

    // Descartes directos.
    public int AntiguedadMinimaDias { get; set; } = 90;
    public decimal TasaDevolucionMaxima { get; set; } = 0.30m;

    /// <summary>Puntaje minimo para ser elegible; tambien es el umbral del nivel REGULAR.</summary>
    public int PuntajeMinimo { get; set; } = 60;

    // Factores del puntaje (maximo de puntos y referencia que da el maximo).
    public decimal PuntosPuntualidad { get; set; } = 40m;
    public decimal PuntosPuntualidadSinPagos { get; set; } = 20m;
    public decimal PuntosMonto { get; set; } = 30m;
    public decimal MontoReferencia { get; set; } = 12000m;
    public decimal PuntosAntiguedad { get; set; } = 8m;
    public int DiasAntiguedadReferencia { get; set; } = 730;
    public decimal PuntosFrecuencia { get; set; } = 7m;
    public int ComprasReferencia { get; set; } = 12;
    public decimal PuntosDevoluciones { get; set; } = 10m;
    public decimal PuntosIncidentes { get; set; } = 5m;
    public decimal PuntosUnIncidente { get; set; } = 2.5m;

    // Niveles y limites.
    public int UmbralExcelente { get; set; } = 90;
    public decimal LimiteExcelente { get; set; } = 7500m;
    public int UmbralBueno { get; set; } = 75;
    public decimal LimiteBueno { get; set; } = 3500m;
    public decimal LimiteRegular { get; set; } = 1500m;
}
