using SgoLogic.Contracts.Enumeraciones;
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

    public Task<(IReadOnlyList<Existencia> Elementos, int Total)> ListarAsync(
        string? ubicacionId,
        TipoUbicacion? tipoUbicacion,
        int pagina,
        int tamano,
        CancellationToken cancelacion)
    {
        var filtradas = _existencias
            .Where(existencia => ubicacionId is null || existencia.UbicacionId == ubicacionId)
            .Where(existencia => tipoUbicacion is null || existencia.TipoUbicacion == tipoUbicacion)
            .OrderBy(existencia => existencia.Sku)
            .ThenBy(existencia => existencia.UbicacionId)
            .ToList();

        var elementos = filtradas.Skip((pagina - 1) * tamano).Take(tamano).ToList();

        return Task.FromResult<(IReadOnlyList<Existencia>, int)>((elementos, filtradas.Count));
    }

    public Task<IReadOnlyList<Existencia>> ListarPorUbicacionAsync(
        string ubicacionId,
        CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<Existencia>>(
            _existencias
                .Where(existencia => existencia.UbicacionId == ubicacionId)
                .OrderBy(existencia => existencia.Sku)
                .ToList());

    public Task<IReadOnlyList<UbicacionResumenDto>> ResumirUbicacionesAsync(CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<UbicacionResumenDto>>(
            _existencias
                .GroupBy(existencia => new { existencia.UbicacionId, existencia.TipoUbicacion })
                .Select(grupo => new UbicacionResumenDto(
                    grupo.Key.UbicacionId,
                    grupo.Key.TipoUbicacion,
                    grupo.Count(),
                    grupo.Sum(existencia => existencia.CantidadFisica),
                    grupo.Sum(existencia => existencia.CantidadReservada),
                    grupo.Sum(existencia => existencia.Disponible)))
                .OrderBy(resumen => resumen.UbicacionId)
                .ToList());

    public Task AgregarAsync(Existencia existencia, CancellationToken cancelacion)
    {
        LlamadasAgregar++;
        _existencias.Add(existencia);
        return Task.CompletedTask;
    }
}
