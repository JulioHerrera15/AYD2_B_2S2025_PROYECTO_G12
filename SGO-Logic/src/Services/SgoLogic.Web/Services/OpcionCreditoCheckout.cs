namespace SgoLogic.Web.Services;

public enum EstadoOpcionCredito
{
    /// <summary>a) Seleccionable: tiene disponible para el total.</summary>
    Disponible,

    /// <summary>b) Cupo insuficiente, no califica, sin historial o linea en pausa: texto del backend.</summary>
    NoDisponibleParaCliente,

    /// <summary>c) Credito no respondio a tiempo.</summary>
    ServicioNoDisponible,

    /// <summary>d) Sin sesion.</summary>
    SinSesion
}

/// <summary>
/// Como se pinta "Credito MegaStore" en el checkout. La opcion se muestra siempre;
/// solo es seleccionable en <see cref="EstadoOpcionCredito.Disponible" />.
/// </summary>
public sealed record OpcionCreditoCheckout(
    EstadoOpcionCredito Estado,
    string? Titulo,
    string Mensaje,
    decimal? Disponible = null,
    DateTime? FechaLimitePago = null,
    bool EnPausa = false)
{
    public const string MensajeServicioNoDisponible =
        "En este momento no contamos con la opción de crédito. Si deseas pagar con crédito, vuelve a intentarlo más tarde.";

    public const string MensajeSinSesion = "Inicia sesión para ver si tienes crédito disponible.";

    public bool Seleccionable => Estado == EstadoOpcionCredito.Disponible;

    /// <summary>Traduce la consulta al gateway al estado que ve el cliente.</summary>
    public static OpcionCreditoCheckout Desde(ResultadoOfertaCreditoModel resultado)
    {
        if (resultado.Estado == EstadoConsultaCredito.SesionInvalida)
        {
            return new OpcionCreditoCheckout(EstadoOpcionCredito.SinSesion, null, MensajeSinSesion);
        }

        if (resultado.Estado != EstadoConsultaCredito.Ok || resultado.Oferta is null)
        {
            return new OpcionCreditoCheckout(EstadoOpcionCredito.ServicioNoDisponible, null, MensajeServicioNoDisponible);
        }

        var oferta = resultado.Oferta;
        return oferta.Ofrecer
            ? new OpcionCreditoCheckout(EstadoOpcionCredito.Disponible, oferta.Titulo, oferta.Mensaje, oferta.Disponible, oferta.FechaPagoEstimada)
            : new OpcionCreditoCheckout(
                EstadoOpcionCredito.NoDisponibleParaCliente,
                oferta.Titulo,
                oferta.Mensaje,
                EnPausa: string.Equals(oferta.Estado, "LINEA_BLOQUEADA", StringComparison.OrdinalIgnoreCase));
    }
}
