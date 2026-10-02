using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Tabla de transiciones validas del ciclo de vida de la orden (ADR-002, saga
/// orquestada). Es la unica autoridad sobre que movimiento esta permitido: ningun
/// servicio de aplicacion decide transiciones por su cuenta.
/// </summary>
public static class MaquinaEstadosOrden
{
    private static readonly IReadOnlyDictionary<EstadoOrden, IReadOnlySet<EstadoOrden>> Permitidas =
        new Dictionary<EstadoOrden, IReadOnlySet<EstadoOrden>>
        {
            [EstadoOrden.CREADA] = Conjunto(
                EstadoOrden.RESERVA_SOLICITADA,
                EstadoOrden.RECHAZADA,
                EstadoOrden.CANCELADA),

            [EstadoOrden.RESERVA_SOLICITADA] = Conjunto(
                EstadoOrden.INVENTARIO_RESERVADO,
                EstadoOrden.RECHAZADA,
                EstadoOrden.CANCELADA_RESERVA_VENCIDA,
                EstadoOrden.CANCELADA),

            [EstadoOrden.INVENTARIO_RESERVADO] = Conjunto(
                EstadoOrden.COBRO_SOLICITADO,
                EstadoOrden.EVALUACION_CREDITO_SOLICITADA,
                EstadoOrden.CANCELADA_RESERVA_VENCIDA,
                EstadoOrden.CANCELADA),

            // Ruta de credito a 30 dias: reemplaza COBRO_SOLICITADO..PAGO_APLICADO.
            // RECHAZADA cubre tanto el rechazo de Credito como el timeout de la saga.
            [EstadoOrden.EVALUACION_CREDITO_SOLICITADA] = Conjunto(
                EstadoOrden.CREDITO_APROBADO,
                EstadoOrden.RECHAZADA,
                EstadoOrden.CANCELADA_RESERVA_VENCIDA,
                EstadoOrden.CANCELADA),

            [EstadoOrden.CREDITO_APROBADO] = Conjunto(
                EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL),

            [EstadoOrden.COBRO_SOLICITADO] = Conjunto(
                EstadoOrden.PAGO_APLICADO,
                EstadoOrden.PAGO_PENDIENTE,
                EstadoOrden.CANCELADA),

            [EstadoOrden.PAGO_PENDIENTE] = Conjunto(
                EstadoOrden.PAGO_APLICADO,
                EstadoOrden.COBRO_SOLICITADO,
                EstadoOrden.CANCELADA_RESERVA_VENCIDA,
                EstadoOrden.CANCELADA),

            [EstadoOrden.PAGO_APLICADO] = Conjunto(
                EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL),

            [EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL] = Conjunto(
                EstadoOrden.CONFIRMADA,
                EstadoOrden.CANCELADA),

            [EstadoOrden.CONFIRMADA] = Conjunto(
                EstadoOrden.DESPACHO_SOLICITADO,
                EstadoOrden.CANCELADA),

            [EstadoOrden.DESPACHO_SOLICITADO] = Conjunto(
                EstadoOrden.EN_PREPARACION,
                EstadoOrden.CANCELADA),

            [EstadoOrden.EN_PREPARACION] = Conjunto(
                EstadoOrden.EN_TRANSITO,
                EstadoOrden.LISTA_PARA_RECOGER,
                EstadoOrden.CANCELADA),

            [EstadoOrden.EN_TRANSITO] = Conjunto(
                EstadoOrden.ENTREGADA),

            [EstadoOrden.LISTA_PARA_RECOGER] = Conjunto(
                EstadoOrden.ENTREGADA),

            [EstadoOrden.ENTREGADA] = Conjunto(
                EstadoOrden.DEVOLUCION_EN_PROCESO),

            [EstadoOrden.DEVOLUCION_EN_PROCESO] = Conjunto(
                EstadoOrden.DEVUELTA,
                EstadoOrden.ENTREGADA),

            [EstadoOrden.DEVUELTA] = Conjunto(),
            [EstadoOrden.CANCELADA_RESERVA_VENCIDA] = Conjunto(),
            [EstadoOrden.CANCELADA] = Conjunto(),
            [EstadoOrden.RECHAZADA] = Conjunto()
        };

    /// <summary>Estados desde los que la orden ya no se mueve nunca mas.</summary>
    public static bool EsTerminal(EstadoOrden estado) => Permitidas[estado].Count == 0;

    public static bool EsTransicionValida(EstadoOrden desde, EstadoOrden hacia) =>
        Permitidas[desde].Contains(hacia);

    public static IReadOnlySet<EstadoOrden> TransicionesDesde(EstadoOrden desde) => Permitidas[desde];

    private static IReadOnlySet<EstadoOrden> Conjunto(params EstadoOrden[] estados) =>
        new HashSet<EstadoOrden>(estados);
}
