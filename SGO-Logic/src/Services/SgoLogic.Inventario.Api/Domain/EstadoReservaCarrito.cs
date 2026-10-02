namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Ciclo de vida de una retencion de stock mientras el producto esta en el carrito.</summary>
public enum EstadoReservaCarrito
{
    /// <summary>Retiene unidades en <see cref="Existencia.CantidadReservada" /> hasta <see cref="ReservaCarrito.ExpiraEn" />.</summary>
    ACTIVA,

    /// <summary>El TTL vencio y las unidades volvieron al disponible. Se puede reactivar si todavia hay stock.</summary>
    EXPIRADA,

    /// <summary>El cliente quito el producto del carrito (o la linea quedo descartada). Terminal.</summary>
    LIBERADA,

    /// <summary>La orden del carrito tomo estas unidades: la reserva de la orden las reemplazo. Terminal.</summary>
    ADOPTADA
}
