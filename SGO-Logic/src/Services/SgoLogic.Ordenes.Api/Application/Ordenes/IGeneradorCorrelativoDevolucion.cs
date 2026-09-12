namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Entrega el siguiente correlativo de devolucion. La implementacion usa la secuencia
/// de PostgreSQL <c>ordenes.devolucion_correlativo</c>; el servicio lo formatea a 6
/// digitos para armar el id DEV-&lt;anio&gt;-&lt;correlativo&gt;.
/// </summary>
public interface IGeneradorCorrelativoDevolucion
{
    Task<long> SiguienteAsync(CancellationToken cancelacion);
}
