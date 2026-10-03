namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>
/// Seccion "Pagos:Reembolsos": reembolsos por cancelacion de orden y de abonos a
/// credito que procesa <see cref="ServicioReembolsosPendientes" />.
/// </summary>
public sealed class ReembolsosOptions
{
    public const string Seccion = "Pagos:Reembolsos";

    /// <summary>Llamadas al proveedor ante falla tecnica antes de dejar el reembolso FALLIDO.</summary>
    public int MaxIntentos { get; set; } = 8;

    /// <summary>Espera tras la primera falla tecnica; se duplica en cada intento.</summary>
    public int BackoffBaseSegundos { get; set; } = 5;

    /// <summary>Tope de la espera entre intentos.</summary>
    public int BackoffMaximoSegundos { get; set; } = 60;

    /// <summary>Cada cuanto el worker barre los reembolsos PENDIENTE vencidos.</summary>
    public int IntervaloSegundos { get; set; } = 5;

    /// <summary>Reembolsos por pasada del worker.</summary>
    public int Lote { get; set; } = 50;
}
