namespace SgoLogic.Facturacion.Api.Application.Facturacion;

/// <summary>
/// Entrega el siguiente correlativo del documento fiscal desde la secuencia de
/// PostgreSQL. Se consume una sola vez, al crear el documento; los reintentos
/// reusan el numero ya asignado y nunca vuelven a llamar aca.
/// </summary>
public interface IGeneradorCorrelativoFiscal
{
    Task<long> SiguienteAsync(CancellationToken cancelacion);
}
