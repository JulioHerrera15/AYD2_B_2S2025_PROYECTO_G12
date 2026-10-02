using Microsoft.EntityFrameworkCore;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Domain;

namespace SgoLogic.Notificaciones.Api.Infrastructure;

/// <summary>Implementacion de <see cref="IRepositorioNotificacionesPendientes" /> sobre <see cref="NotificacionesDbContext" />.</summary>
public sealed class RepositorioNotificacionesPendientes : IRepositorioNotificacionesPendientes
{
    private readonly NotificacionesDbContext _contexto;

    public RepositorioNotificacionesPendientes(NotificacionesDbContext contexto)
    {
        _contexto = contexto;
    }

    public void Agregar(NotificacionPendiente notificacion) => _contexto.NotificacionesPendientes.Add(notificacion);

    public async Task<IReadOnlyList<NotificacionPendiente>> ListarParaReintentarAsync(
        DateTime ahora,
        int maximo,
        CancellationToken cancelacion) =>
        await _contexto.NotificacionesPendientes
            .Where(n => n.Estado == EstadoNotificacionPendiente.PENDIENTE && n.ProximoIntentoEn <= ahora)
            .OrderBy(n => n.ProximoIntentoEn)
            .Take(maximo)
            .ToListAsync(cancelacion);

    public Task GuardarCambiosAsync(CancellationToken cancelacion) => _contexto.SaveChangesAsync(cancelacion);
}
