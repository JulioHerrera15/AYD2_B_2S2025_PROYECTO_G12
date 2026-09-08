using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Tests.Application.Dobles;

/// <summary>
/// Doble en memoria de <see cref="IRepositorioMensajesSalientes" />. Acumula lo
/// que la aplicacion intenta escribir en el outbox.
/// </summary>
public sealed class RepositorioMensajesSalientesFalso : IRepositorioMensajesSalientes
{
    public List<MensajeSaliente> Mensajes { get; } = new();

    public Task AgregarAsync(MensajeSaliente mensaje, CancellationToken cancelacion)
    {
        Mensajes.Add(mensaje);
        return Task.CompletedTask;
    }
}
