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
    IReadOnlyList<FactorCreditoDto> Factores,
    IReadOnlyList<CompraCreditoDto> Compras,
    decimal TotalPorPagar = 0m,
    DateTime? ProximoVencimiento = null,
    IReadOnlyList<AvisoCreditoDto>? Avisos = null);

/// <summary>Aviso destacado de "Mi credito". Tipo: POR_VENCER, VENCIDA o LINEA_EN_PAUSA (no se muestra).</summary>
public sealed record AvisoCreditoDto(string Tipo, string Titulo, string Mensaje, string Recomendacion, string? CuentaId);

/// <summary>Un pago de una compra a credito, para el historial. Sin codigos internos para mostrar.</summary>
public sealed record AbonoHistorialDto(
    string AbonoId,
    string Referencia,
    DateTime Fecha,
    decimal Monto,
    string? Tarjeta,
    string Estado,
    string EtiquetaEstado);

/// <summary>
/// Estado de un abono para la Web. Estado es el codigo; Titulo y Mensaje son para el
/// cliente. PuedeReintentar solo con el abono PENDIENTE.
/// </summary>
public sealed record AbonoDto(
    string Id,
    string Referencia,
    string CuentaId,
    string Estado,
    bool EnCurso,
    string Titulo,
    string Mensaje,
    string Recomendacion,
    decimal Monto,
    string Moneda,
    decimal SaldoPendienteCuenta,
    bool PuedeReintentar);

public sealed record CrearAbonoRequest(string? CuentaId, decimal Monto, string? TokenMedioPago);

public sealed record ReintentarAbonoRequest(string? TokenMedioPago);

/// <summary>
/// Una compra a credito del cliente. Estado es el codigo (VIGENTE, PAGADA, VENCIDA,
/// ANULADA); EtiquetaEstado es lo que se muestra. DiasRestantes solo para VIGENTE.
/// </summary>
public sealed record CompraCreditoDto(
    string NumeroOrden,
    string OrdenId,
    decimal Monto,
    decimal SaldoPendiente,
    DateTime EmitidaEn,
    DateTime VenceEn,
    int? DiasRestantes,
    string Estado,
    string EtiquetaEstado,
    string CuentaId = "",
    bool PuedePagar = false,
    string? AbonoEnCursoId = null,
    bool AbonoEnCursoPuedeReintentar = false,
    IReadOnlyList<AbonoHistorialDto>? Abonos = null);

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
