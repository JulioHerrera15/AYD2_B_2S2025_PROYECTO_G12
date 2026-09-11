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
}
