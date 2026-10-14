using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Ubicacion fisica de MegaStore (tienda o centro de distribucion) con la metadata
/// que la vista de existencias no tiene: nombre visible, pais, departamento,
/// municipio y coordenadas. <see cref="Id" /> es el mismo identificador que usa
/// <see cref="Existencia.UbicacionId" />, asi que ambas tablas se cruzan sin
/// traduccion.
///
/// Las coordenadas son opcionales: una sucursal sin ellas sigue siendo valida,
/// solo que no participa del orden por distancia (<see cref="SelectorSucursalCercana" />).
///
/// <see cref="Departamento" /> es la division de primer nivel del pais: departamento
/// en Guatemala y El Salvador, provincia en Costa Rica.
/// </summary>
public sealed class Sucursal
{
    private Sucursal()
    {
        Id = string.Empty;
        Nombre = string.Empty;
        Pais = string.Empty;
        Departamento = string.Empty;
        Municipio = string.Empty;
    }

    public Sucursal(
        string id,
        string nombre,
        TipoUbicacion tipoUbicacion,
        string pais,
        string departamento,
        string municipio,
        double? latitud,
        double? longitud,
        bool activa = true)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("El identificador de la sucursal es obligatorio.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("El nombre de la sucursal es obligatorio.", nameof(nombre));
        }

        if (string.IsNullOrWhiteSpace(pais))
        {
            throw new ArgumentException("El pais de la sucursal es obligatorio.", nameof(pais));
        }

        if (latitud is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitud), "La latitud debe estar entre -90 y 90.");
        }

        if (longitud is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitud), "La longitud debe estar entre -180 y 180.");
        }

        Id = id;
        Nombre = nombre;
        TipoUbicacion = tipoUbicacion;
        Pais = pais;
        Departamento = departamento ?? string.Empty;
        Municipio = municipio ?? string.Empty;
        Latitud = latitud;
        Longitud = longitud;
        Activa = activa;
    }

    /// <summary>Igual al <c>ubicacion_id</c> de la tabla de existencias.</summary>
    public string Id { get; private set; }

    public string Nombre { get; private set; }

    public TipoUbicacion TipoUbicacion { get; private set; }

    public string Pais { get; private set; }

    public string Departamento { get; private set; }

    public string Municipio { get; private set; }

    public double? Latitud { get; private set; }

    public double? Longitud { get; private set; }

    public bool Activa { get; private set; }

    public bool TieneCoordenadas => Latitud is not null && Longitud is not null;

    public bool MismoPaisQue(Sucursal otra)
    {
        ArgumentNullException.ThrowIfNull(otra);
        return string.Equals(Pais, otra.Pais, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Distancia en kilometros sobre la superficie terrestre (formula de Haversine)
    /// hasta <paramref name="otra" />, o null si a alguna de las dos le faltan
    /// coordenadas.
    /// </summary>
    public double? DistanciaKmA(Sucursal otra)
    {
        ArgumentNullException.ThrowIfNull(otra);

        if (!TieneCoordenadas || !otra.TieneCoordenadas)
        {
            return null;
        }

        return DistanciaHaversineKm(Latitud!.Value, Longitud!.Value, otra.Latitud!.Value, otra.Longitud!.Value);
    }

    private const double RadioTierraKm = 6371.0;

    public static double DistanciaHaversineKm(double latitud1, double longitud1, double latitud2, double longitud2)
    {
        static double ARadianes(double grados) => grados * Math.PI / 180.0;

        var deltaLatitud = ARadianes(latitud2 - latitud1);
        var deltaLongitud = ARadianes(longitud2 - longitud1);

        var a = Math.Pow(Math.Sin(deltaLatitud / 2), 2)
                + Math.Cos(ARadianes(latitud1)) * Math.Cos(ARadianes(latitud2)) * Math.Pow(Math.Sin(deltaLongitud / 2), 2);

        return 2 * RadioTierraKm * Math.Asin(Math.Sqrt(a));
    }
}
