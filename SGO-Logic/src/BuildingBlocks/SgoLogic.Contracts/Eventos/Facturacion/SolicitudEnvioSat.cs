namespace SgoLogic.Contracts.Eventos.Facturacion;

/// <summary>
/// Mensaje interno de Facturacion que viaja por el canal MOM dedicado a la SAT
/// (exchange <c>sgo.sat</c>, ADR-011 y TOPOLOGIA-MENSAJERIA seccion 1). No es un
/// evento de dominio de <c>sgo.eventos</c>: solo pide enviar a certificar un
/// documento fiscal ya generado y persistido. Lo produce el worker de reintentos
/// de Facturacion y lo consume el enviador a la SAT del mismo contexto.
/// </summary>
public static class SolicitudEnvioSat
{
    public const string EventType = "SolicitudEnvioSat";

    /// <summary>Routing key sobre el exchange direct <c>sgo.sat</c>.</summary>
    public const string RoutingKey = "sat.certificacion";

    public const string AggregateType = "DocumentoFiscal";
}

/// <summary>
/// Contenido de <c>data</c> de <see cref="SolicitudEnvioSat" />. <c>Intento</c> es
/// el numero de intento que representa este mensaje (el enviador descarta un
/// mensaje cuyo <c>Intento</c> ya quedo atras, para que un reencolado duplicado no
/// cuente dos veces contra MaxIntentos).
/// </summary>
public sealed record SolicitudEnvioSatData(
    string DocumentoFiscalId,
    string OrdenId,
    int Intento);
