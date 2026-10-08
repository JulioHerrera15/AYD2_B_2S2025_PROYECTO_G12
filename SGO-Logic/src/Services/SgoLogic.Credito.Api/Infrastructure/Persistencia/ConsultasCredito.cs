using Microsoft.EntityFrameworkCore;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia;

/// <summary>
/// Lectura de la oferta: perfil con LEFT JOIN a linea e historial, sin seguimiento
/// de cambios y en un solo SELECT.
/// </summary>
public sealed class ConsultasCredito : IConsultasCredito
{
    private readonly CreditoDbContext _contexto;

    public ConsultasCredito(CreditoDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<ResumenCredito?> ObtenerResumenAsync(string clienteId, CancellationToken cancelacion) =>
        (from perfil in _contexto.Perfiles.AsNoTracking()
         where perfil.ClienteId == clienteId
         join linea in _contexto.Lineas.AsNoTracking() on perfil.ClienteId equals linea.ClienteId into lineas
         from linea in lineas.DefaultIfEmpty()
         join historial in _contexto.Historiales.AsNoTracking() on perfil.ClienteId equals historial.ClienteId into historiales
         from historial in historiales.DefaultIfEmpty()
         select new ResumenCredito(
             perfil.ClienteId,
             perfil.Nivel,
             perfil.Elegible,
             perfil.CodigoMotivo,
             perfil.PuntosPuntualidad,
             perfil.PuntosMonto,
             perfil.PuntosAntiguedad,
             perfil.PuntosFrecuencia,
             perfil.PuntosDevoluciones,
             perfil.PuntosIncidentes,
             perfil.CalculadoEn,
             linea == null ? null : (decimal?)linea.LimiteAprobado,
             linea == null ? null : (decimal?)linea.SaldoUtilizado,
             linea == null ? null : linea.Moneda,
             linea == null ? null : (EstadoLineaCredito?)linea.Estado,
             linea == null ? null : (DateTime?)linea.ActualizadoEn,
             historial == null ? null : (DateTime?)historial.ClienteDesde))
        .FirstOrDefaultAsync(cancelacion);

    public async Task<IReadOnlyList<CompraCredito>> ListarComprasAsync(string clienteId, CancellationToken cancelacion) =>
        await _contexto.CuentasPorCobrar.AsNoTracking()
            .Where(cuenta => cuenta.ClienteId == clienteId)
            .Select(cuenta => new CompraCredito(
                cuenta.OrdenId,
                cuenta.NumeroOrden,
                cuenta.MontoOriginal,
                cuenta.SaldoPendiente,
                cuenta.EmitidaEn,
                cuenta.VenceEn,
                cuenta.Estado,
                cuenta.Id,
                cuenta.PagadaEn,
                cuenta.PagoPuntual,
                cuenta.MotivoAnulacion))
            .ToListAsync(cancelacion);

    public async Task<IReadOnlyList<AbonoCredito>> ListarAbonosAsync(string clienteId, CancellationToken cancelacion) =>
        await _contexto.Abonos.AsNoTracking()
            .Where(abono => abono.ClienteId == clienteId)
            .ToListAsync(cancelacion);

    public async Task<IReadOnlyList<MovimientoCuenta>> ListarMovimientosAsync(string clienteId, CancellationToken cancelacion) =>
        await _contexto.Movimientos.AsNoTracking()
            .Where(movimiento => movimiento.ClienteId == clienteId)
            .OrderBy(movimiento => movimiento.Fecha)
            .ToListAsync(cancelacion);
}
