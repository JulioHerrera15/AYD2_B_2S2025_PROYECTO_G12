namespace SgoLogic.Despacho.Api.Domain;

/// <summary>
/// Ciclo de vida de un despacho. Local al contexto Despacho: Ordenes tiene su
/// propio <c>EstadoOrden</c> y lo mapea al consumir los eventos de aqui.
/// </summary>
public enum EstadoDespacho
{
    /// <summary>Recien creado, aun no se resolvio de donde sale.</summary>
    ORIGEN_PENDIENTE,

    /// <summary>El cliente recoge en tienda: no hay guia que pedir (HU-020).</summary>
    RECOGIDA_TIENDA,

    /// <summary>Se llamo a LogiExpress y se espera respuesta.</summary>
    GUIA_SOLICITADA,

    /// <summary>LogiExpress emitio la guia.</summary>
    GUIA_ASIGNADA,

    /// <summary>LogiExpress no respondio. El despacho fisico continua igual (HU-011).</summary>
    GUIA_PENDIENTE,

    /// <summary>El operador esta alistando el pedido en la ubicacion de origen (HU-019).</summary>
    EN_PREPARACION,

    /// <summary>El transportista lo lleva (HU-012).</summary>
    EN_TRANSITO,

    /// <summary>Listo en tienda para que el cliente pase (HU-020).</summary>
    LISTA_PARA_RECOGER,

    /// <summary>Terminal: el cliente lo recibio.</summary>
    ENTREGADA,

    /// <summary>Problema reportado por el transportista (HU-021).</summary>
    INCIDENCIA,

    /// <summary>Terminal: la orden se cancelo y el despacho se anulo.</summary>
    ANULADA
}
