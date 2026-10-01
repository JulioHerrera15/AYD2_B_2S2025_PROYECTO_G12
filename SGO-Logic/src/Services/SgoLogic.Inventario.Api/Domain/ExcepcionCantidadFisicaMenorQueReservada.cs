namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Se lanza al ajustar la cantidad fisica de una existencia por debajo de lo que
/// ya esta comprometido (reservas de ordenes y retenciones de carrito). Aceptarlo
/// dejaria el disponible en negativo y prometidas unidades que no existen. El
/// endpoint REST la traduce a 409.
/// </summary>
public sealed class ExcepcionCantidadFisicaMenorQueReservada : Exception
{
    public ExcepcionCantidadFisicaMenorQueReservada(string sku, string ubicacionId, int cantidadFisica, int cantidadReservada)
        : base($"No se puede dejar {sku} en {ubicacionId} con {cantidadFisica} unidades fisicas: " +
               $"hay {cantidadReservada} reservadas.")
    {
        Sku = sku;
        UbicacionId = ubicacionId;
        CantidadFisica = cantidadFisica;
        CantidadReservada = cantidadReservada;
    }

    public string Sku { get; }

    public string UbicacionId { get; }

    public int CantidadFisica { get; }

    public int CantidadReservada { get; }
}
