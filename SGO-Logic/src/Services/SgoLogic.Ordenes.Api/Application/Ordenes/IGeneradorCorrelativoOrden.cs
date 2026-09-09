namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Entrega el siguiente correlativo de orden. La implementacion usa la secuencia
/// de PostgreSQL <c>ordenes.orden_correlativo</c>; el servicio lo formatea a 6
/// digitos para armar el id ORD-&lt;anio&gt;-&lt;correlativo&gt; y el numero de
/// orden visible.
/// </summary>
public interface IGeneradorCorrelativoOrden
{
    Task<long> SiguienteAsync(CancellationToken cancelacion);
}
