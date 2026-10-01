namespace SgoLogic.ConsultaEstado.Api.Domain;

/// <summary>
/// Vista materializada de una Orden, mantenida por el consumidor de eventos.
/// Es el modelo de lectura de Consulta de Estado — vive en Redis (cache caliente)
/// y en PostgreSQL (fallback frio / historial paginado).
///
/// REGLA DE FUENTE AUTORITATIVA: el campo Estado SOLO lo escriben los eventos
/// OrdenCreada (ev. 1) y OrdenEstadoCambiado (ev. 2). Los eventos 8, 9, 10, 11 y
/// 12 enriquecen otros campos pero NUNCA tocan Estado.
/// </summary>
public sealed class OrdenVista
{
    public string OrdenId { get; set; } = default!;
    public string? NumeroOrden { get; set; }
    public string? ClienteId { get; set; }

    // ── Estado (solo eventos 1 y 2 escriben aqui) ─────────────────────────────
    public string? Estado { get; set; }
    public string? EstadoAnterior { get; set; }

    // ── Datos de creacion (evento 1) ──────────────────────────────────────────
    public string? Canal { get; set; }
    public string? MetodoEntrega { get; set; }
    public string? UbicacionSolicitadaId { get; set; }
    public string? Moneda { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }

    // ── Datos enriquecidos (eventos 8, 9, 10, 11, 12) ────────────────────────
    public string? PagoId { get; set; }
    public string? TiendaDestinoId { get; set; }
    public string? DocumentoFiscalId { get; set; }
    public string? SerieDocumentoFiscal { get; set; }
    public string? NumeroDocumentoFiscal { get; set; }
    public string? MotivoUltimoCambio { get; set; }
    public bool? RequiereReembolso { get; set; }
    public decimal? MontoReembolso { get; set; }

    // ── Datos de Despacho (eventos de SgoLogic.Despacho — NO tocan Estado) ───
    /// <summary>Id del despacho asignado por el contexto Despacho.</summary>
    public string? DespachoId { get; set; }
    /// <summary>Número de guía emitida por LogiExpress (null si es recogida en tienda o aún no asignada).</summary>
    public string? GuiaDespacho { get; set; }
    /// <summary>Estado interno del despacho (EN_PREPARACION, EN_TRANSITO, ENTREGADO, etc.).</summary>
    public string? EstadoDespacho { get; set; }
    /// <summary>
    /// Ubicacion desde la que sale el pedido (tienda o centro de distribucion), tomada
    /// de OrigenDespachoDeterminado. Es donde Inventario desconto las unidades.
    /// </summary>
    public string? UbicacionOrigenId { get; set; }

    // ── Control de ordenamiento (por aggregateId) ─────────────────────────────
    /// <summary>Ultimo sequence aplicado para este aggregateId. Parte de la regla last-writer-wins.</summary>
    public int UltimoSequence { get; set; }

    public DateTime CreadaEn { get; set; }
    public DateTime ActualizadaEn { get; set; }
}

/// <summary>
/// Incidencia ligada a una Orden. Una orden puede tener 0..N incidencias.
/// Vive completamente dentro de ConsultaEstado — no genera eventos hacia otros
/// contextos (pregunta abierta 2 del instrucciones.txt resuelta: tratarla local
/// hasta que el equipo decida lo contrario).
/// </summary>
public sealed class Incidencia
{
    public Guid Id { get; set; }
    public string OrdenId { get; set; } = default!;
    public string Descripcion { get; set; } = default!;
    public string Estado { get; set; } = default!;
    public string? Resolucion { get; set; }
    public DateTime CreadaEn { get; set; }
    public DateTime? ActualizadaEn { get; set; }
}
