namespace SgoLogic.Credito.Api.Application;

/// <summary>Estados de la oferta de credito que ven la Web y el checkout.</summary>
public static class EstadosOfertaCredito
{
    public const string Disponible = "DISPONIBLE";
    public const string CupoInsuficiente = "CUPO_INSUFICIENTE";
    public const string NoElegible = "NO_ELEGIBLE";
    public const string SinHistorial = "SIN_HISTORIAL";
    public const string LineaBloqueada = "LINEA_BLOQUEADA";
}

/// <summary>Valoracion de un factor segun la proporcion de puntos obtenidos.</summary>
public static class ValoracionesFactor
{
    public const string Favorable = "FAVORABLE";
    public const string Aceptable = "ACEPTABLE";
    public const string AMejorar = "A_MEJORAR";
}

/// <summary>
/// Respuesta de GET /api/credito/elegibilidad. No lleva el puntaje. Ofrecer es true
/// solo si el cliente es elegible, la linea esta ACTIVA y el disponible cubre el
/// monto (o no se envio monto).
/// </summary>
public sealed record OfertaCreditoDto(
    string Estado,
    bool Ofrecer,
    string? CodigoMotivo,
    string Titulo,
    string Mensaje,
    string Recomendacion,
    decimal LimiteAprobado,
    decimal Disponible,
    decimal? MontoSolicitado,
    string Moneda,
    int PlazoDias,
    DateTime? FechaPagoEstimada,
    DateTime? CalculadoEn);

/// <summary>Un factor del perfil explicado al cliente, sin puntos.</summary>
public sealed record FactorCreditoDto(string Nombre, string Valoracion, string Descripcion);

/// <summary>Respuesta de GET /api/credito/mi-credito. No lleva el puntaje.</summary>
public sealed record MiCreditoDto(
    string Estado,
    string Nivel,
    string EtiquetaNivel,
    string Titulo,
    string Mensaje,
    string Recomendacion,
    decimal LimiteAprobado,
    decimal SaldoUtilizado,
    decimal Disponible,
    string Moneda,
    int PlazoDias,
    DateTime? ActualizadoEn,
    IReadOnlyList<FactorCreditoDto> Factores);

/// <summary>
/// Resultado de aplicar un historial. Aplicado es false si se ignoro por traer una
/// fecha de corte igual o anterior a la ya aplicada; en ese caso el perfil es el
/// vigente. Este DTO si trae el puntaje: solo sale por el endpoint interno.
/// </summary>
public sealed record ResultadoAplicacionHistorial(
    string ClienteId,
    bool Aplicado,
    bool HuboCambios,
    int Puntaje,
    string Nivel,
    bool Elegible,
    string? CodigoMotivo,
    decimal LimiteAprobado,
    string Moneda,
    string VersionPolitica,
    DateTime FechaCorteHistorial,
    DateTime CalculadoEn);
