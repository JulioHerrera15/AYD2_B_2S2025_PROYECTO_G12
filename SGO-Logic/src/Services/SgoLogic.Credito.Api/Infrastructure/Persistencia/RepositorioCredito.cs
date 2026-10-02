using Microsoft.EntityFrameworkCore;
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
    }

    public void DescartarCambios() => _contexto.ChangeTracker.Clear();

    public async Task<ResultadoReinicioDemo> ReiniciarDemoAsync(DateTime ahora, CancellationToken cancelacion)
    {
        await using var transaccion = await _contexto.Database.BeginTransactionAsync(cancelacion);

        var cuentas = await _contexto.CuentasPorCobrar.ExecuteDeleteAsync(cancelacion);
        var decisiones = await _contexto.Decisiones.ExecuteDeleteAsync(cancelacion);
        var lineas = await _contexto.Lineas.ExecuteUpdateAsync(
            actualizar => actualizar
                .SetProperty(linea => linea.SaldoUtilizado, 0m)
                .SetProperty(linea => linea.ActualizadoEn, ahora)
                .SetProperty(linea => linea.Version, linea => linea.Version + 1),
            cancelacion);

        await transaccion.CommitAsync(cancelacion);
        _contexto.ChangeTracker.Clear();

        return new ResultadoReinicioDemo(cuentas, decisiones, lineas);
    }
}
