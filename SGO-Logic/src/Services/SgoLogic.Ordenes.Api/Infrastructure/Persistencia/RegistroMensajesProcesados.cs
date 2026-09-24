using Microsoft.EntityFrameworkCore;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;

namespace SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IRegistroMensajesProcesados" /> sobre
/// <see cref="OrdenesDbContext" />. Comparte la misma instancia scoped del
/// DbContext que la unidad de trabajo, asi que la marca que agrega
/// <see cref="Registrar" /> se persiste en la misma transaccion que la transicion
/// de la orden y las filas de outbox.
/// </summary>
public sealed class RegistroMensajesProcesados : IRegistroMensajesProcesados
{
    private readonly OrdenesDbContext _contexto;

    public RegistroMensajesProcesados(OrdenesDbContext contexto)
    {
        _contexto = contexto;
    }

    public Task<bool> YaProcesadoAsync(string messageId, CancellationToken cancelacion) =>
        _contexto.MensajesProcesados
            .AsNoTracking()
            .AnyAsync(mensaje => mensaje.MessageId == messageId, cancelacion);

    public void Registrar(string messageId, string tipoEvento) =>
        _contexto.MensajesProcesados.Add(new MensajeProcesado(messageId, tipoEvento, DateTime.UtcNow));
}
