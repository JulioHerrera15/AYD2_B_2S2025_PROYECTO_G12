using Microsoft.EntityFrameworkCore;
using Npgsql;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion EF de <see cref="IRepositorioCredito" />. Comparte el
/// <see cref="CreditoDbContext" /> scoped con IOutboxWriter, asi que el
/// GuardarCambiosAsync confirma el evento junto con el cambio de negocio. Traduce el
/// choque de Version a <see cref="ExcepcionConcurrenciaCredito" />.
/// </summary>
public sealed class RepositorioCredito : IRepositorioCredito
{
    private readonly CreditoDbContext _contexto;

    public RepositorioCredito(CreditoDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<HistorialCliente?> ObtenerHistorialAsync(string clienteId, CancellationToken cancelacion) =>
        _contexto.Historiales.FirstOrDefaultAsync(historial => historial.ClienteId == clienteId, cancelacion);

    public Task<PerfilCrediticio?> ObtenerPerfilAsync(string clienteId, CancellationToken cancelacion) =>
        _contexto.Perfiles.FirstOrDefaultAsync(perfil => perfil.ClienteId == clienteId, cancelacion);

    public Task<LineaCredito?> ObtenerLineaAsync(string clienteId, CancellationToken cancelacion) =>
        _contexto.Lineas.FirstOrDefaultAsync(linea => linea.ClienteId == clienteId, cancelacion);

    public void AgregarHistorial(HistorialCliente historial) => _contexto.Historiales.Add(historial);

    public void AgregarPerfil(PerfilCrediticio perfil) => _contexto.Perfiles.Add(perfil);

    public void AgregarLinea(LineaCredito linea) => _contexto.Lineas.Add(linea);

    public Task<bool> HayHistorialesAsync(CancellationToken cancelacion) =>
        _contexto.Historiales.AnyAsync(cancelacion);

    public Task<DecisionCredito?> ObtenerDecisionAsync(string ordenId, CancellationToken cancelacion) =>
        _contexto.Decisiones.FirstOrDefaultAsync(decision => decision.OrdenId == ordenId, cancelacion);

    public Task<CuentaPorCobrar?> ObtenerCuentaPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        _contexto.CuentasPorCobrar.FirstOrDefaultAsync(cuenta => cuenta.OrdenId == ordenId, cancelacion);

    public void AgregarDecision(DecisionCredito decision) => _contexto.Decisiones.Add(decision);

    public void AgregarCuenta(CuentaPorCobrar cuenta) => _contexto.CuentasPorCobrar.Add(cuenta);

    public Task<CuentaPorCobrar?> ObtenerCuentaAsync(Guid cuentaId, CancellationToken cancelacion) =>
        _contexto.CuentasPorCobrar.FirstOrDefaultAsync(cuenta => cuenta.Id == cuentaId, cancelacion);

    public async Task<IReadOnlyList<CuentaPorCobrar>> ListarCuentasClienteAsync(string clienteId, CancellationToken cancelacion) =>
        await _contexto.CuentasPorCobrar.Where(cuenta => cuenta.ClienteId == clienteId).ToListAsync(cancelacion);

    public async Task<IReadOnlyList<Guid>> ListarCuentasParaRecordatorioAsync(DateTime limite, int maximo, CancellationToken cancelacion) =>
        await _contexto.CuentasPorCobrar.AsNoTracking()
            .Where(cuenta => cuenta.Estado == EstadoCuentaPorCobrar.VIGENTE
                && cuenta.SaldoPendiente > 0m
                && cuenta.RecordatorioEnviadoEn == null
                && cuenta.VenceEn <= limite)
            .OrderBy(cuenta => cuenta.VenceEn)
            .Select(cuenta => cuenta.Id)
            .Take(maximo)
            .ToListAsync(cancelacion);

    public async Task<IReadOnlyList<Guid>> ListarCuentasPorVencerseAsync(DateTime ahora, int maximo, CancellationToken cancelacion) =>
        await _contexto.CuentasPorCobrar.AsNoTracking()
            .Where(cuenta => cuenta.Estado == EstadoCuentaPorCobrar.VIGENTE
                && cuenta.SaldoPendiente > 0m
                && cuenta.VenceEn < ahora)
            .OrderBy(cuenta => cuenta.VenceEn)
            .Select(cuenta => cuenta.Id)
            .Take(maximo)
            .ToListAsync(cancelacion);

    public Task<AbonoCredito?> ObtenerAbonoAsync(Guid abonoId, CancellationToken cancelacion) =>
        _contexto.Abonos.FirstOrDefaultAsync(abono => abono.Id == abonoId, cancelacion);

    public Task<bool> HayAbonoEnCursoAsync(Guid cuentaId, CancellationToken cancelacion) =>
        _contexto.Abonos.AnyAsync(
            abono => abono.CuentaId == cuentaId
                && (abono.Estado == EstadoAbonoCredito.SOLICITADO || abono.Estado == EstadoAbonoCredito.PENDIENTE),
            cancelacion);

    public async Task<IReadOnlyList<AbonoCredito>> ListarAbonosAplicadosAsync(Guid cuentaId, CancellationToken cancelacion) =>
        await _contexto.Abonos
            .Where(abono => abono.CuentaId == cuentaId && abono.Estado == EstadoAbonoCredito.APLICADO)
            .ToListAsync(cancelacion);

    public void AgregarAbono(AbonoCredito abono) => _contexto.Abonos.Add(abono);

    public Task<bool> HayAbonosCobradosAsync(Guid cuentaId, CancellationToken cancelacion) =>
        _contexto.Abonos.AnyAsync(
            abono => abono.CuentaId == cuentaId
                && (abono.Estado == EstadoAbonoCredito.APLICADO || abono.Estado == EstadoAbonoCredito.REEMBOLSADO),
            cancelacion);

    public async Task<IReadOnlyList<Guid>> ListarAbonosReembolsoSinSolicitudAsync(int maximo, CancellationToken cancelacion) =>
        await _contexto.Abonos.AsNoTracking()
            .Where(abono => abono.Estado == EstadoAbonoCredito.REEMBOLSO_PENDIENTE && abono.ReembolsoSolicitadoEn == null)
            .OrderBy(abono => abono.SolicitadoEn)
            .Select(abono => abono.Id)
            .Take(maximo)
            .ToListAsync(cancelacion);

    public Task<bool> ExisteMovimientoAsync(TipoMovimientoCuenta tipo, string referencia, CancellationToken cancelacion) =>
        _contexto.Movimientos.AnyAsync(movimiento => movimiento.Tipo == tipo && movimiento.Referencia == referencia, cancelacion);

    public void AgregarMovimiento(MovimientoCuenta movimiento) => _contexto.Movimientos.Add(movimiento);

    public async Task<long> SiguienteCorrelativoAbonoAsync(CancellationToken cancelacion) =>
        await _contexto.Database
            .SqlQueryRaw<long>("SELECT nextval('credito.abono_correlativo') AS \"Value\"")
            .SingleAsync(cancelacion);

    public async Task<IReadOnlyList<string>> ListarClientesConHistorialAsync(CancellationToken cancelacion) =>
        await _contexto.Historiales.AsNoTracking().Select(historial => historial.ClienteId).ToListAsync(cancelacion);

    public async Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        try
        {
            await _contexto.SaveChangesAsync(cancelacion);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ExcepcionConcurrenciaCredito(
                "La linea o la cuenta fue modificada por otra operacion concurrente.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ExcepcionDuplicadoCredito("Un indice unico rechazo el cambio.", ex);
        }
    }

    public void DescartarCambios() => _contexto.ChangeTracker.Clear();

    public async Task<ResultadoReinicioDemo> ReiniciarDemoAsync(DateTime ahora, CancellationToken cancelacion)
    {
        await using var transaccion = await _contexto.Database.BeginTransactionAsync(cancelacion);

        var movimientos = await _contexto.Movimientos.ExecuteDeleteAsync(cancelacion);
        var abonos = await _contexto.Abonos.ExecuteDeleteAsync(cancelacion);
        var cuentas = await _contexto.CuentasPorCobrar.ExecuteDeleteAsync(cancelacion);
        var decisiones = await _contexto.Decisiones.ExecuteDeleteAsync(cancelacion);
        var lineas = await _contexto.Lineas.ExecuteUpdateAsync(
            actualizar => actualizar
                .SetProperty(linea => linea.SaldoUtilizado, 0m)
                .SetProperty(
                    linea => linea.Estado,
                    linea => linea.Estado == EstadoLineaCredito.BLOQUEADA
                        ? (linea.LimiteAprobado > 0m ? EstadoLineaCredito.ACTIVA : EstadoLineaCredito.INACTIVA)
                        : linea.Estado)
                .SetProperty(linea => linea.ActualizadoEn, ahora)
                .SetProperty(linea => linea.Version, linea => linea.Version + 1),
            cancelacion);

        await transaccion.CommitAsync(cancelacion);
        _contexto.ChangeTracker.Clear();

        return new ResultadoReinicioDemo(cuentas, decisiones, lineas, abonos, movimientos);
    }
}
