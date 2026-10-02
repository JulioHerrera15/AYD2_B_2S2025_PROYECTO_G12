namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Se lanza al consumir una reserva cuando la existencia no tiene reservadas (o
/// fisicas) las unidades que la reserva dice retener. Consumir igual liberaria
/// unidades comprometidas por otras ordenes, asi que se rechaza sin tocar nada.
/// </summary>
public sealed class ExcepcionConsumoReservaInconsistente : Exception
{
    public ExcepcionConsumoReservaInconsistente(
        string sku,
        string ubicacionId,
        int solicitado,
        int reservada,
        int fisica)
        : base($"No se puede consumir {solicitado} unidades de {sku} en {ubicacionId}: " +
               $"la existencia tiene {reservada} reservadas y {fisica} fisicas.")
    {
        Sku = sku;
        UbicacionId = ubicacionId;
        Solicitado = solicitado;
        Reservada = reservada;
        Fisica = fisica;
    }

    public string Sku { get; }

    public string UbicacionId { get; }

    public int Solicitado { get; }

    public int Reservada { get; }

    public int Fisica { get; }
}
