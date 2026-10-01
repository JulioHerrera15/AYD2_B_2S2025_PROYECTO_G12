using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Se lanza cuando el cliente pide cancelar una orden en un estado que no lo
/// admite: el cobro esta en curso (reintentable en unos segundos) o la orden ya
/// salio a ruta, se entrego o ya estaba cancelada. El endpoint la traduce a 409.
/// </summary>
public sealed class ExcepcionCancelacionNoPermitida : Exception
{
    public const string MensajePagoEnCurso = "La orden esta procesando el pago, intenta de nuevo en unos segundos";

    public const string MensajeNoCancelable = "La orden ya no puede cancelarse";

    public ExcepcionCancelacionNoPermitida(string ordenId, EstadoOrden estado, bool pagoEnCurso)
        : base(pagoEnCurso ? MensajePagoEnCurso : MensajeNoCancelable)
    {
        OrdenId = ordenId;
        Estado = estado;
        PagoEnCurso = pagoEnCurso;
    }

    public string OrdenId { get; }

    public EstadoOrden Estado { get; }

    /// <summary>True si el rechazo es transitorio (cobro en curso).</summary>
    public bool PagoEnCurso { get; }
}
