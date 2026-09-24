using SgoLogic.Inventario.Api.Application.Inventario;

namespace SgoLogic.Inventario.Tests.Application.Dobles;

/// <summary>Doble de <see cref="IGeneradorCorrelativoReserva" />: correlativos consecutivos en memoria.</summary>
public sealed class GeneradorCorrelativoReservaFalso : IGeneradorCorrelativoReserva
{
    private long _siguiente;

    public GeneradorCorrelativoReservaFalso(long inicial = 1)
    {
        _siguiente = inicial;
    }

    public Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        Task.FromResult(_siguiente++);
}
