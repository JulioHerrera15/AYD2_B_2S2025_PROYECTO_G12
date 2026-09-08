namespace SgoLogic.BuildingBlocks.Messaging;

/// <summary>
/// Nombres fijos de la topologia de mensajeria del proyecto (congelados en
/// docs/contratos/sgo-eventos-envelope-v1.md, seccion 5). Los exchanges y la dead
/// letter son transversales; la cola propia de cada contexto la declara ese
/// contexto con <see cref="DeclaradorColas"/>.
/// </summary>
public static class TopologiaMensajeria
{
    /// <summary>Exchange topic de eventos de dominio (pub/sub). Routing key: contexto.entidad.hecho-en-kebab-case.</summary>
    public const string ExchangeEventos = "sgo.eventos";

    /// <summary>Exchange direct del canal MOM de certificacion fiscal (interno de Facturacion, ADR-011).</summary>
    public const string ExchangeSat = "sgo.sat";

    /// <summary>Exchange direct del canal de lote hacia el ERP (interno de Integracion ERP, ADR-008).</summary>
    public const string ExchangeErp = "sgo.erp";

    /// <summary>Fanout de dead letter compartido por todas las colas quorum del proyecto.</summary>
    public const string ExchangeDeadLetter = "sgo.eventos.dlx";

    /// <summary>Cola quorum de descarte, sin TTL: recibe todo lo que agota reintentos en cualquier cola.</summary>
    public const string ColaDeadLetter = "sgo.eventos.dlq";

    /// <summary>Limite por defecto de reintentos de entrega (x-delivery-limit) para colas quorum propias de un contexto.</summary>
    public const int LimiteReintentosPorDefecto = 5;
}