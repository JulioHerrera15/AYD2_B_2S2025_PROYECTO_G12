namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>Seccion "Reintento" de la configuracion. Validez del enlace de reintento que se genera cuando el pago queda PENDIENTE.</summary>
public sealed class ReintentoOptions
{
    public const string Seccion = "Reintento";

    public int ExpiracionMinutos { get; set; } = 60;

    public TimeSpan Expiracion => TimeSpan.FromMinutes(ExpiracionMinutos);
}
