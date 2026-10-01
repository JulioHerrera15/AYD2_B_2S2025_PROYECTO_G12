namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Parametros del contexto Inventario. El TTL de reserva lo fija Inventario, no
/// Ordenes (supuesto 2 de ordenes-eventos-v1): el valor que llega en el evento se
/// toma como sugerencia y se recorta al rango [minimo, maximo].
/// </summary>
public sealed class InventarioOptions
{
    public const string Seccion = "Inventario";

    /// <summary>TTL aplicado cuando la solicitud no trae uno valido. 15 minutos.</summary>
    public int TtlSegundosPorDefecto { get; set; } = 900;

    /// <summary>Piso del TTL aceptado. Evita reservas que expiran antes de poder cobrarse.</summary>
    public int TtlSegundosMinimo { get; set; } = 60;

    /// <summary>Techo del TTL aceptado. Evita que una compra abandonada retenga stock por horas.</summary>
    public int TtlSegundosMaximo { get; set; } = 3600;

    /// <summary>
    /// Cada cuanto barre <c>ExpiracionReservasWorker</c> la tabla de reservas buscando
    /// TTL vencidos (HU-002). El barrido contra PostgreSQL es el mecanismo autoritativo:
    /// a diferencia de las notificaciones de expiracion de Redis, que son fire-and-forget,
    /// una reserva vencida sigue vencida aunque el servicio haya estado caido.
    /// </summary>
    public int ExpiracionPollingMs { get; set; } = 5000;

    /// <summary>Cuantas reservas vencidas procesa como maximo cada ciclo del barrido.</summary>
    public int ExpiracionBatchSize { get; set; } = 20;

    /// <summary>
    /// Cuanto retiene stock un producto en el carrito sin que el cliente toque el
    /// carrito. Cualquier cambio en el carrito renueva el plazo de todas sus lineas.
    /// Variable de entorno INVENTARIO_CARRITO_TTL_SEGUNDOS en docker-compose.
    /// </summary>
    public int CarritoTtlSegundos { get; set; } = 900;

    /// <summary>
    /// <see cref="CarritoTtlSegundos" /> recortado a [60, 3600]: un carrito no debe
    /// retener stock por horas, ni vencer antes de que el cliente alcance a pagar.
    /// </summary>
    public int CarritoTtlEfectivo => Math.Clamp(CarritoTtlSegundos > 0 ? CarritoTtlSegundos : 900, 60, 3600);

    /// <summary>Recorta <paramref name="solicitado" /> al rango permitido; usa el valor por defecto si no es positivo.</summary>
    public int TtlEfectivo(int solicitado)
    {
        var baseTtl = solicitado > 0 ? solicitado : TtlSegundosPorDefecto;
        return Math.Clamp(baseTtl, TtlSegundosMinimo, TtlSegundosMaximo);
    }
}
