using SgoLogic.Inventario.Api.Application.Inventario;
using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioExistencias" />.</summary>
public sealed class RepositorioExistenciasFalso : IRepositorioExistencias
{
    private readonly List<Existencia> _existencias = new();

    public int LlamadasAgregar { get; private set; }

    public RepositorioExistenciasFalso Precargar(params Existencia[] existencias)
    {
        _existencias.AddRange(existencias);
        return this;
    }

    public Task<IReadOnlyList<Existencia>> ListarPorSkusAsync(
        IReadOnlyCollection<string> skus,
        CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Existencia>>(
            _existencias.Where(existencia => skus.Contains(existencia.Sku)).ToList());

    public Task<IReadOnlyList<Existencia>> ListarPorSkuAsync(string sku, CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Existencia>>(
            _existencias.Where(existencia => existencia.Sku == sku).ToList());

    public Task<Existencia?> ObtenerAsync(string sku, string ubicacionId, CancellationToken cancelacion) =>
        Task.FromResult(_existencias.FirstOrDefault(
            existencia => existencia.Sku == sku && existencia.UbicacionId == ubicacionId));

    public Task AgregarAsync(Existencia existencia, CancellationToken cancelacion)
    {
        LlamadasAgregar++;
        _existencias.Add(existencia);
        return Task.CompletedTask;
    }
}
