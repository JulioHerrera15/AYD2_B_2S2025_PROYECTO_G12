using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Puerto de persistencia del outbox del contexto. Se escribe en la misma unidad
/// de trabajo que el agregado Orden para que el evento y el cambio de estado sean
/// atomicos.
/// </summary>
public interface IRepositorioMensajesSalientes
{
    /// <summary>Registra un mensaje saliente en la unidad de trabajo.</summary>
    Task AgregarAsync(MensajeSaliente mensaje, CancellationToken cancelacion);
}
