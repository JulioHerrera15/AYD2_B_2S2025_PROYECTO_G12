using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Se lanza cuando la solicitud de creacion elige un medio de pago que se cobra con
/// token (TARJETA) y no trae <c>tokenMedioPago</c>. El token lo emite PayKlik al
/// tokenizar la tarjeta en el navegador; sin el, Pagos no podria cobrar. El endpoint
/// la traduce a 400 con codigo "token_medio_pago_requerido".
/// </summary>
public sealed class ExcepcionTokenMedioPagoRequerido : Exception
{
    public ExcepcionTokenMedioPagoRequerido(MedioPago medioPago)
        : base($"El medio de pago {medioPago} requiere tokenMedioPago: tokenice la tarjeta con PayKlik antes de crear la orden.")
    {
        MedioPago = medioPago;
    }

    public MedioPago MedioPago { get; }
}
