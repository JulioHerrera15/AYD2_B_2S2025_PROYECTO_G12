namespace SgoLogic.Credito.Api.Application;

/// <summary>Opciones generales del servicio (seccion Credito). La politica va en Credito:Politica.</summary>
public sealed class CreditoOptions
{
    public const string Seccion = "Credito";

    /// <summary>Al arrancar, si historial_cliente esta vacia, aplica Datos/historial-demo.json.</summary>
    public bool SembrarDemo { get; set; }

    /// <summary>Habilita POST /interno/credito/historiales (demo y pruebas). Falso por defecto.</summary>
    public bool EndpointsInternos { get; set; }
}
