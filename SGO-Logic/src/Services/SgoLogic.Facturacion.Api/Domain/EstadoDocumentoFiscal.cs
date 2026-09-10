namespace SgoLogic.Facturacion.Api.Domain;

/// <summary>
/// Estado del agregado <see cref="DocumentoFiscal" />. Lista cerrada.
/// </summary>
public enum EstadoDocumentoFiscal
{
    /// <summary>Documento generado y persistido con su XML, serie y correlativo; pendiente de certificar ante la SAT.</summary>
    PENDIENTE,

    /// <summary>La SAT devolvio folio y numero de autorizacion. Terminal feliz.</summary>
    CERTIFICADO,

    /// <summary>Se agoto Sat:MaxIntentos. Estado terminal de alerta para revision manual (ADR-011, CU-04).</summary>
    ALERTA
}
