using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Seccion "MediosPagoPorCanal" de la configuracion (HU-018: seleccion de medio de
/// pago por canal). Cada clave es el nombre de un valor de <see cref="Canal" /> y
/// su valor es la lista de <see cref="MedioPago" /> admitidos en ese canal. Se
/// enlaza directamente sobre el diccionario.
/// </summary>
public sealed class MediosPagoPorCanalOptions : Dictionary<string, List<MedioPago>>
{
    public const string Seccion = "MediosPagoPorCanal";
}
