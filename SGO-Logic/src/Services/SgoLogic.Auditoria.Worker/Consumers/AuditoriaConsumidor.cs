using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.Auditoria.Worker.Application;
using SgoLogic.Auditoria.Worker.Infrastructure;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;

namespace SgoLogic.Auditoria.Worker.Consumers;

/// <summary>
/// Cola propia de Auditoria, ligada con "#" a sgo.eventos: recibe TODOS los
/// eventos de TODOS los contextos, no solo ordenes.#, porque la bitacora solo es
/// completa si audita todo lo que pasa por el bus.
///
/// TData = JsonElement: el envelope se deserializa igual sin importar la forma
/// de "data", asi que un evento de un tipo que Contracts todavia no conoce se
/// persiste igual (no rompe la deserializacion, que es lo unico que podria
/// fallar por un tipo desconocido).
///
/// EventConsumerBase ya resuelve deduplicacion (mensajes_procesados) y ack/nack:
/// si RegistrarAsync o el SaveChangesAsync final fallan (por ejemplo, con la base
/// caida), ManejarAsync propaga la excepcion, EventConsumerBase hace nack con
/// requeue, y el mensaje vuelve a esta misma cola sin perderse hasta agotar
/// x-delivery-limit (entonces cae a sgo.eventos.dlx).
/// </summary>
public sealed class AuditoriaConsumidor : EventConsumerBase<AuditoriaDbContext, JsonElement>
{
    private readonly RegistradorEventoAuditoria _registrador;

    public AuditoriaConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        RegistradorEventoAuditoria registrador,
        ILogger<AuditoriaConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _registrador = registrador;
    }

    protected override string Cola => "auditoria-worker.eventos";

    protected override string NombreServicio => "auditoria-worker";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "#")
    ];

    protected override Task ManejarAsync(
        IntegrationEvent<JsonElement> evento,
        AuditoriaDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        return _registrador.RegistrarAsync(evento, contexto, cancelacion);
    }
}