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

    /// <summary>Recorta <paramref name="solicitado" /> al rango permitido; usa el valor por defecto si no es positivo.</summary>
    public int TtlEfectivo(int solicitado)
    {
        var baseTtl = solicitado > 0 ? solicitado : TtlSegundosPorDefecto;
        return Math.Clamp(baseTtl, TtlSegundosMinimo, TtlSegundosMaximo);
    }
}
