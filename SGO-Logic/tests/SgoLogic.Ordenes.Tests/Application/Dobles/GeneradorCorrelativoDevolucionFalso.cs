using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IGeneradorCorrelativoDevolucion" />. Devuelve correlativos
/// consecutivos en memoria, sin secuencia de base de datos.
/// </summary>
public sealed class GeneradorCorrelativoDevolucionFalso : IGeneradorCorrelativoDevolucion
{
    private long _siguiente;

    public GeneradorCorrelativoDevolucionFalso(long inicial = 1)
    {
        _siguiente = inicial;
    }

    public Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        Task.FromResult(_siguiente++);
}
