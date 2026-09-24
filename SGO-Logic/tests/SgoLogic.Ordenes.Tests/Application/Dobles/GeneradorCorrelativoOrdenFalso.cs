using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IGeneradorCorrelativoOrden" />. Devuelve correlativos
/// consecutivos en memoria, sin secuencia de base de datos.
/// </summary>
public sealed class GeneradorCorrelativoOrdenFalso : IGeneradorCorrelativoOrden
{
    private long _siguiente;

    public GeneradorCorrelativoOrdenFalso(long inicial = 1)
    {
        _siguiente = inicial;
    }

    public Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        Task.FromResult(_siguiente++);
}
