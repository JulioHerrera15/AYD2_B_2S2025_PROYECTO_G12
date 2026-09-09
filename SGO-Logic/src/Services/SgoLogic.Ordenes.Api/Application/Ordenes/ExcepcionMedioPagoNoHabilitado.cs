using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Se lanza cuando la solicitud de creacion trae un medio de pago que no esta
/// habilitado para el canal enviado (HU-018). El endpoint la traduce a 400 con
/// codigo "medio_pago_no_habilitado".
/// </summary>
public sealed class ExcepcionMedioPagoNoHabilitado : Exception
{
    public ExcepcionMedioPagoNoHabilitado(Canal canal, MedioPago medioPago)
        : base($"El medio de pago {medioPago} no esta habilitado para el canal {canal}.")
    {
        Canal = canal;
        MedioPago = medioPago;
    }

    public Canal Canal { get; }

    public MedioPago MedioPago { get; }
}
