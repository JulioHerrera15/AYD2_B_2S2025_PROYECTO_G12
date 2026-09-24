namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Entrega el siguiente correlativo de reserva. La implementacion usa la secuencia
/// de PostgreSQL <c>inventario.reserva_correlativo</c>; el servicio lo formatea a 6
/// digitos para armar el id RSV-&lt;anio&gt;-&lt;correlativo&gt;.
/// </summary>
public interface IGeneradorCorrelativoReserva
{
    Task<long> SiguienteAsync(CancellationToken cancelacion);
}
