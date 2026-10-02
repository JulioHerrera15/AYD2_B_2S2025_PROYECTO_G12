using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Retencion de stock de un SKU mientras esta en el carrito, antes de que exista la
/// orden. Es un agregado aparte de <see cref="Reserva" /> a proposito: la reserva
/// pertenece a una orden y viaja por la saga; la retencion pertenece a un carrito
/// (cart:{usuarioId} o guest:{sesionId}) y nunca sale de Inventario.
///
/// Mientras esta ACTIVA sus unidades suman a <see cref="Existencia.CantidadReservada" />,
/// asi que el disponible que ven los demas clientes ya las descuenta. Quien la crea,
/// ajusta o libera es el que mueve la existencia: este agregado solo lleva el
/// registro de cuanto, donde y hasta cuando.
///
/// Hay a lo sumo una retencion vigente (ACTIVA o EXPIRADA) por carrito y SKU: una
/// EXPIRADA que vuelve a pedirse se reactiva sobre la misma fila con
/// <see cref="Reasignar" />. El indice unico parcial sobre (carrito_id, sku) WHERE
/// estado = 'ACTIVA' es la red de seguridad en la base.
/// </summary>
public sealed class ReservaCarrito
{
    public const int LargoMaximoCarritoId = 120;

    private ReservaCarrito()
    {
        CarritoId = string.Empty;
        Sku = string.Empty;
        UbicacionId = string.Empty;
    }

    private ReservaCarrito(string carritoId, string sku)
    {
        if (string.IsNullOrWhiteSpace(carritoId))
        {
            throw new ArgumentException("El identificador del carrito es obligatorio.", nameof(carritoId));
        }

        if (carritoId.Length > LargoMaximoCarritoId)
        {
            throw new ArgumentException(
                $"El identificador del carrito no puede exceder {LargoMaximoCarritoId} caracteres.",
                nameof(carritoId));
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        Id = Guid.NewGuid();
        CarritoId = carritoId;
        Sku = sku;
        UbicacionId = string.Empty;
        Version = 0;
    }

    /// <summary>Crea una retencion ACTIVA que vence <paramref name="ttlSegundos" /> despues de <paramref name="ahora" />.</summary>
    public static ReservaCarrito Crear(
        string carritoId,
        string sku,
        string ubicacionId,
        TipoUbicacion tipoUbicacion,
        int cantidad,
        string? ubicacionPreferidaId,
        int ttlSegundos,
        DateTime ahora)
    {
        var retencion = new ReservaCarrito(carritoId, sku)
        {
            CreadaEn = ahora.ToUniversalTime()
        };

        retencion.Asignar(ubicacionId, tipoUbicacion, cantidad, ubicacionPreferidaId, ttlSegundos, ahora);
        retencion.Version = 0;
        return retencion;
    }

    public Guid Id { get; private set; }

    /// <summary>El mismo identificador que usa Auth para la clave del carrito en Redis.</summary>
    public string CarritoId { get; private set; }

    public string Sku { get; private set; }

    public string UbicacionId { get; private set; }

    public TipoUbicacion TipoUbicacion { get; private set; }

    /// <summary>
    /// Sucursal que el cliente tenia elegida al apartar. Si es distinta de
    /// <see cref="UbicacionId" />, el stock quedo apartado en otra tienda porque la
    /// suya no tenia suficiente (<see cref="EnOtraSucursal" />).
    /// </summary>
    public string? UbicacionPreferidaId { get; private set; }

    public int Cantidad { get; private set; }

    public EstadoReservaCarrito Estado { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public DateTime ExpiraEn { get; private set; }

    /// <summary>Token de concurrencia (columna <c>version</c>), igual que en <see cref="Existencia" />.</summary>
    public int Version { get; private set; }

    public bool EstaActiva => Estado == EstadoReservaCarrito.ACTIVA;

    /// <summary>ACTIVA o EXPIRADA: la linea sigue siendo parte del carrito.</summary>
    public bool EstaVigente => Estado is EstadoReservaCarrito.ACTIVA or EstadoReservaCarrito.EXPIRADA;

    public bool EnOtraSucursal =>
        !string.IsNullOrWhiteSpace(UbicacionPreferidaId)
        && !string.Equals(UbicacionPreferidaId, UbicacionId, StringComparison.Ordinal);

    public bool HaVencido(DateTime ahora) => ahora.ToUniversalTime() >= ExpiraEn;

    /// <summary>
    /// Deja la retencion ACTIVA con la ubicacion y cantidad indicadas y le renueva el
    /// TTL. Vale tanto para ajustar una ACTIVA (cambio de cantidad o de tienda) como
    /// para reactivar una EXPIRADA. Quien llama ya movio la existencia.
    /// </summary>
    public void Reasignar(
        string ubicacionId,
        TipoUbicacion tipoUbicacion,
        int cantidad,
        string? ubicacionPreferidaId,
        int ttlSegundos,
        DateTime ahora)
    {
        if (!EstaVigente)
        {
            throw new InvalidOperationException(
                $"La retencion {Id} esta en {Estado} y ya no puede reasignarse.");
        }

        Asignar(ubicacionId, tipoUbicacion, cantidad, ubicacionPreferidaId, ttlSegundos, ahora);
    }

    /// <summary>Extiende el TTL de una retencion ACTIVA. En cualquier otro estado no hace nada.</summary>
    public void Renovar(int ttlSegundos, DateTime ahora)
    {
        ValidarTtl(ttlSegundos);

        if (!EstaActiva)
        {
            return;
        }

        ExpiraEn = ahora.ToUniversalTime().AddSeconds(ttlSegundos);
        Version += 1;
    }

    /// <summary>El TTL vencio: la retencion deja de retener. Idempotente si ya no estaba ACTIVA.</summary>
    public void MarcarExpirada()
    {
        if (EstaActiva)
        {
            Estado = EstadoReservaCarrito.EXPIRADA;
            Version += 1;
        }
    }

    /// <summary>
    /// El producto salio del carrito. Desde ACTIVA las unidades vuelven al disponible
    /// (lo hace quien llama); desde EXPIRADA solo se cierra la linea. Idempotente.
    /// </summary>
    public void Liberar()
    {
        if (EstaVigente)
        {
            Estado = EstadoReservaCarrito.LIBERADA;
            Version += 1;
        }
    }

    /// <summary>La orden del carrito tomo las unidades. Solo desde ACTIVA.</summary>
    public void Adoptar()
    {
        if (!EstaActiva)
        {
            throw new InvalidOperationException(
                $"La retencion {Id} esta en {Estado}; solo una ACTIVA puede adoptarse.");
        }

        Estado = EstadoReservaCarrito.ADOPTADA;
        Version += 1;
    }

    /// <summary>Mueve la linea a otro carrito (invitado que inicia sesion). Solo lineas vigentes.</summary>
    public void TransferirA(string carritoDestinoId)
    {
        if (string.IsNullOrWhiteSpace(carritoDestinoId) || carritoDestinoId.Length > LargoMaximoCarritoId)
        {
            throw new ArgumentException("El carrito destino no es valido.", nameof(carritoDestinoId));
        }

        if (!EstaVigente)
        {
            throw new InvalidOperationException(
                $"La retencion {Id} esta en {Estado} y ya no puede transferirse.");
        }

        CarritoId = carritoDestinoId;
        Version += 1;
    }

    private void Asignar(
        string ubicacionId,
        TipoUbicacion tipoUbicacion,
        int cantidad,
        string? ubicacionPreferidaId,
        int ttlSegundos,
        DateTime ahora)
    {
        if (string.IsNullOrWhiteSpace(ubicacionId))
        {
            throw new ArgumentException("El identificador de la ubicacion es obligatorio.", nameof(ubicacionId));
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad retenida debe ser mayor que cero.");
        }

        ValidarTtl(ttlSegundos);

        UbicacionId = ubicacionId;
        TipoUbicacion = tipoUbicacion;
        Cantidad = cantidad;
        UbicacionPreferidaId = string.IsNullOrWhiteSpace(ubicacionPreferidaId) ? null : ubicacionPreferidaId;
        Estado = EstadoReservaCarrito.ACTIVA;
        ExpiraEn = ahora.ToUniversalTime().AddSeconds(ttlSegundos);
        Version += 1;
    }

    private static void ValidarTtl(int ttlSegundos)
    {
        if (ttlSegundos <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlSegundos), "El TTL de la retencion debe ser mayor que cero.");
        }
    }
}
