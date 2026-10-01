using SgoLogic.Ordenes.Api.Application.Ordenes;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IClienteCarrito" />. Solo registra los clienteId que se
/// pidio vaciar, sin llamar a ningun servicio real.
/// </summary>
public sealed class ClienteCarritoFalso : IClienteCarrito
{
    public List<string> ClientesVaciados { get; } = new();

    public Task VaciarAsync(string clienteId, CancellationToken cancelacion)
    {
        ClientesVaciados.Add(clienteId);
        return Task.CompletedTask;
    }
}