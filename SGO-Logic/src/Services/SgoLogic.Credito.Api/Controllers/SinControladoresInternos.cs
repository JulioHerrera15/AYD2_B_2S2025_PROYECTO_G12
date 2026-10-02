using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace SgoLogic.Credito.Api.Controllers;

/// <summary>
/// Quita <see cref="CreditoInternoController" /> de la aplicacion cuando
/// Credito:EndpointsInternos es false: la ruta /interno no existe (404), no solo
/// queda protegida.
/// </summary>
public sealed class SinControladoresInternos : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        var internos = feature.Controllers.Where(tipo => tipo.AsType() == typeof(CreditoInternoController)).ToList();
        foreach (var tipo in internos)
        {
            feature.Controllers.Remove(tipo);
        }
    }
}
