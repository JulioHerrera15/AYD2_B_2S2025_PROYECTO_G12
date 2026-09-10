using SgoLogic.Facturacion.Api.Application.Facturacion;

namespace SgoLogic.Facturacion.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IGeneradorCorrelativoFiscal" />: entrega correlativos
/// consecutivos desde <paramref name="inicial" /> y cuenta cuantas veces se pidio
/// uno. Sirve para verificar que un reintento NO vuelve a pedir correlativo.
/// </summary>
public sealed class GeneradorCorrelativoFiscalFalso : IGeneradorCorrelativoFiscal
{
    private long _siguiente;

    public GeneradorCorrelativoFiscalFalso(long inicial = 1)
    {
        _siguiente = inicial;
    }

    public int Llamadas { get; private set; }

    public Task<long> SiguienteAsync(CancellationToken cancelacion)
    {
        Llamadas++;
        return Task.FromResult(_siguiente++);
    }
}
