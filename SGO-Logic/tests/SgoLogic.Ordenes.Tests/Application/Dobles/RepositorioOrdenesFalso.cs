using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IRepositorioOrdenes" />. No hay base de datos:
/// guarda las ordenes en diccionarios y cuenta las altas.
/// </summary>
public sealed class RepositorioOrdenesFalso : IRepositorioOrdenes
{
    private readonly Dictionary<string, Orden> _porId = new();
    private readonly Dictionary<string, Orden> _porNumero = new();
    private readonly Dictionary<string, List<Devolucion>> _devolucionesPorOrden = new();

    public int LlamadasAgregar { get; private set; }

    public void Precargar(Orden orden)
    {
        _porId[orden.Id] = orden;
        _porNumero[orden.NumeroOrden] = orden;
    }

    public Task<Orden?> ObtenerPorIdAsync(string id, CancellationToken cancelacion) =>
        Task.FromResult(_porId.TryGetValue(id, out var orden) ? orden : null);

    public Task<Orden?> ObtenerPorNumeroAsync(string numeroOrden, CancellationToken cancelacion) =>
        Task.FromResult(_porNumero.TryGetValue(numeroOrden, out var orden) ? orden : null);

    public Task AgregarAsync(Orden orden, CancellationToken cancelacion)
    {
        LlamadasAgregar++;
        Precargar(orden);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Orden>> ObtenerConAlertaFiscalAsync(CancellationToken cancelacion)
    {
        var resultado = _porId.Values
            .Where(o => o.AlertaFiscal)
            .OrderByDescending(o => o.AlertaFiscalEn)
            .ToList();
        return Task.FromResult<IReadOnlyList<Orden>>(resultado);
    }

    public Task<IReadOnlyList<Devolucion>> ObtenerDevolucionesPorOrdenAsync(string ordenId, CancellationToken cancelacion)
    {
        var resultado = _devolucionesPorOrden.TryGetValue(ordenId, out var lista)
            ? lista.ToList()
            : new List<Devolucion>();
        return Task.FromResult<IReadOnlyList<Devolucion>>(resultado);
    }

    public Task<Devolucion?> ObtenerDevolucionPorIdAsync(string devolucionId, CancellationToken cancelacion)
    {
        var resultado = _devolucionesPorOrden.Values
            .SelectMany(lista => lista)
            .FirstOrDefault(d => d.Id == devolucionId);
        return Task.FromResult(resultado);
    }

    public Task AgregarDevolucionAsync(Devolucion devolucion, CancellationToken cancelacion)
    {
        if (!_devolucionesPorOrden.TryGetValue(devolucion.OrdenOriginalId, out var lista))
        {
            lista = new List<Devolucion>();
            _devolucionesPorOrden[devolucion.OrdenOriginalId] = lista;
        }

        lista.Add(devolucion);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListarEvaluacionesCreditoVencidasAsync(
        DateTime limite,
        int maximo,
        CancellationToken cancelacion)
    {
        var resultado = _porId.Values
            .Where(o => o.Estado == SgoLogic.Contracts.Enumeraciones.EstadoOrden.EVALUACION_CREDITO_SOLICITADA)
            .Select(o => new { o.Id, UltimoCambio = o.Transiciones.Max(t => t.CambiadoEn) })
            .Where(fila => fila.UltimoCambio <= limite)
            .OrderBy(fila => fila.UltimoCambio)
            .Select(fila => fila.Id)
            .Take(maximo)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(resultado);
    }
}
