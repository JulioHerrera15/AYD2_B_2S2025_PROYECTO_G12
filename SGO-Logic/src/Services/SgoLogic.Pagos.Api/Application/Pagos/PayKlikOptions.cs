namespace SgoLogic.Pagos.Api.Application.Pagos;

/// <summary>Seccion "PayKlik" de la configuracion. URL base del proveedor y timeout de la invocacion REST.</summary>
public sealed class PayKlikOptions
{
    public const string Seccion = "PayKlik";

    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutMs { get; set; } = 5000;
}
