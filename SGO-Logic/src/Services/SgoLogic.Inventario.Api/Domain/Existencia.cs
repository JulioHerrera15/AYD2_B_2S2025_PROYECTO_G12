using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Existencia de un SKU en una ubicacion (tienda o centro de distribucion). Es el
/// registro sobre el que actua el bloqueo optimista de ADR-005: <see cref="Version" />
/// es el token de concurrencia y se incrementa en cada movimiento. El conflicto se
/// detecta al escribir, sin bloquear la fila por adelantado.
///
/// La cantidad reservada se lleva aparte de la fisica: <see cref="Disponible" />
/// es lo que puede comprometerse ahora. La liberacion por TTL (HU-002) y el
/// reingreso por devolucion (HU-014) reponen ese disponible.
/// </summary>
public sealed class Existencia
{
    private Existencia()
    {
        Sku = string.Empty;
        UbicacionId = string.Empty;
    }

    public Existencia(string sku, string ubicacionId, TipoUbicacion tipoUbicacion, int cantidadFisica)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(ubicacionId))
        {
            throw new ArgumentException("El identificador de la ubicacion es obligatorio.", nameof(ubicacionId));
        }

        if (cantidadFisica < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidadFisica), "La cantidad fisica no puede ser negativa.");
        }

        Id = Guid.NewGuid();
        Sku = sku;
        UbicacionId = ubicacionId;
        TipoUbicacion = tipoUbicacion;
        CantidadFisica = cantidadFisica;
        CantidadReservada = 0;
        Version = 0;
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public string UbicacionId { get; private set; }

    public TipoUbicacion TipoUbicacion { get; private set; }

    /// <summary>Unidades fisicas en la ubicacion, reservadas o no.</summary>
    public int CantidadFisica { get; private set; }

    /// <summary>Unidades comprometidas por reservas vigentes.</summary>
    public int CantidadReservada { get; private set; }

    /// <summary>
    /// Token de concurrencia (columna <c>version</c>). Arranca en 0 y se incrementa
    /// con cada movimiento. EF Core lo mapea con <c>IsConcurrencyToken()</c>.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Unidades que pueden comprometerse ahora mismo.</summary>
    public int Disponible => CantidadFisica - CantidadReservada;

    /// <summary>
    /// Compromete <paramref name="cantidad" /> unidades. Lanza
    /// <see cref="ExcepcionStockInsuficiente" /> si no hay disponible suficiente,
    /// sin modificar nada.
    /// </summary>
    public void Reservar(int cantidad)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad a reservar debe ser mayor que cero.");
        }

        if (cantidad > Disponible)
        {
            throw new ExcepcionStockInsuficiente(Sku, cantidad, Disponible);
        }

        CantidadReservada += cantidad;
        Version += 1;
    }

    /// <summary>
    /// Devuelve al disponible <paramref name="cantidad" /> unidades antes
    /// reservadas (expiracion por TTL, liberacion por compensacion). Nunca deja la
    /// reservada por debajo de cero.
    /// </summary>
    public void LiberarReserva(int cantidad)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad a liberar debe ser mayor que cero.");
        }

        CantidadReservada = Math.Max(0, CantidadReservada - cantidad);
        Version += 1;
    }

    /// <summary>
    /// Saca del almacen <paramref name="cantidad" /> unidades que estaban reservadas
    /// para una orden: resta exactamente esa cantidad de la fisica y de la
    /// reservada, sin tocar lo comprometido por otras reservas. Lanza
    /// <see cref="ExcepcionConsumoReservaInconsistente" /> si la existencia no tiene
    /// esas unidades reservadas o fisicas, sin modificar nada.
    /// </summary>
    public void ConsumirReservada(int cantidad)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad a consumir debe ser mayor que cero.");
        }

        if (cantidad > CantidadReservada || cantidad > CantidadFisica)
        {
            throw new ExcepcionConsumoReservaInconsistente(Sku, UbicacionId, cantidad, CantidadReservada, CantidadFisica);
        }

        CantidadFisica -= cantidad;
        CantidadReservada -= cantidad;
        Version += 1;
    }

    /// <summary>Ajusta la cantidad fisica (carga inicial, recuento, reingreso).</summary>
    public void AjustarCantidadFisica(int cantidadFisica)
    {
        if (cantidadFisica < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidadFisica), "La cantidad fisica no puede ser negativa.");
        }

        CantidadFisica = cantidadFisica;
        Version += 1;
    }
}
