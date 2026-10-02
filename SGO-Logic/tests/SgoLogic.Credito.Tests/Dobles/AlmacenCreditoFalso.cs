using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Tests.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IRepositorioCredito" /> y
/// <see cref="IConsultasCredito" /> sobre las mismas listas, para que lo que escribe
/// ServicioPerfilCrediticio lo lea ServicioElegibilidad.
/// </summary>
public sealed class AlmacenCreditoFalso : IRepositorioCredito, IConsultasCredito
{
    public List<HistorialCliente> Historiales { get; } = new();
    public List<PerfilCrediticio> Perfiles { get; } = new();
    public List<LineaCredito> Lineas { get; } = new();
    public List<DecisionCredito> Decisiones { get; } = new();
    public List<CuentaPorCobrar> Cuentas { get; } = new();

    public int Descartes { get; private set; }

    public int Guardados { get; private set; }

    public int Consultas { get; private set; }

    public string? UltimoClienteConsultado { get; private set; }

    public Task<HistorialCliente?> ObtenerHistorialAsync(string clienteId, CancellationToken cancelacion) =>
        Task.FromResult(Historiales.FirstOrDefault(h => h.ClienteId == clienteId));

    public Task<PerfilCrediticio?> ObtenerPerfilAsync(string clienteId, CancellationToken cancelacion) =>
        Task.FromResult(Perfiles.FirstOrDefault(p => p.ClienteId == clienteId));

    public Task<LineaCredito?> ObtenerLineaAsync(string clienteId, CancellationToken cancelacion) =>
        Task.FromResult(Lineas.FirstOrDefault(l => l.ClienteId == clienteId));

    public void AgregarHistorial(HistorialCliente historial) => Historiales.Add(historial);

    public void AgregarPerfil(PerfilCrediticio perfil) => Perfiles.Add(perfil);

    public void AgregarLinea(LineaCredito linea) => Lineas.Add(linea);

    public Task<bool> HayHistorialesAsync(CancellationToken cancelacion) => Task.FromResult(Historiales.Count > 0);

    public Task<DecisionCredito?> ObtenerDecisionAsync(string ordenId, CancellationToken cancelacion) =>
        Task.FromResult(Decisiones.FirstOrDefault(d => d.OrdenId == ordenId));

    public Task<CuentaPorCobrar?> ObtenerCuentaPorOrdenAsync(string ordenId, CancellationToken cancelacion) =>
        Task.FromResult(Cuentas.FirstOrDefault(c => c.OrdenId == ordenId));

    public void AgregarDecision(DecisionCredito decision) => Decisiones.Add(decision);

    public void AgregarCuenta(CuentaPorCobrar cuenta) => Cuentas.Add(cuenta);

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        Guardados++;
        return Task.CompletedTask;
    }

    public void DescartarCambios() => Descartes++;

    public Task<ResultadoReinicioDemo> ReiniciarDemoAsync(DateTime ahora, CancellationToken cancelacion)
    {
        var resultado = new ResultadoReinicioDemo(Cuentas.Count, Decisiones.Count, Lineas.Count);
        Cuentas.Clear();
        Decisiones.Clear();
        foreach (var linea in Lineas)
        {
            linea.ReiniciarSaldo(ahora);
        }

        return Task.FromResult(resultado);
    }

    public Task<IReadOnlyList<CompraCredito>> ListarComprasAsync(string clienteId, CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<CompraCredito>>(Cuentas
            .Where(c => c.ClienteId == clienteId)
            .Select(c => new CompraCredito(c.OrdenId, c.NumeroOrden, c.MontoOriginal, c.SaldoPendiente, c.EmitidaEn, c.VenceEn, c.Estado))
            .ToList());

    public Task<ResumenCredito?> ObtenerResumenAsync(string clienteId, CancellationToken cancelacion)
    {
        Consultas++;
        UltimoClienteConsultado = clienteId;

        var perfil = Perfiles.FirstOrDefault(p => p.ClienteId == clienteId);
        if (perfil is null)
        {
            return Task.FromResult<ResumenCredito?>(null);
        }

        var linea = Lineas.FirstOrDefault(l => l.ClienteId == clienteId);
        var historial = Historiales.FirstOrDefault(h => h.ClienteId == clienteId);

        return Task.FromResult<ResumenCredito?>(new ResumenCredito(
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
            linea?.LimiteAprobado,
            linea?.SaldoUtilizado,
            linea?.Moneda,
            linea?.Estado,
            linea?.ActualizadoEn,
            historial?.ClienteDesde));
    }
}
