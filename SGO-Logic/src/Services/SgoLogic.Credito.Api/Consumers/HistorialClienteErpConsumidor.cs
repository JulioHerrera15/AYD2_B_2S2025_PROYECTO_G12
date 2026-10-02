using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;

namespace SgoLogic.Credito.Api.Consumers;

/// <summary>
/// Consume la cola <c>credito.erp</c> (binding a
/// <c>integracion-erp.cliente.historial-actualizado</c>) y recalcula el perfil del
/// cliente. La deduplicacion por eventId la da EventConsumerBase; ademas, un
/// historial con fecha de corte vieja se ignora en el servicio.
///
/// El servicio se llama con confirmar=false: historial, perfil, linea, outbox y la
/// marca de procesado se guardan juntos en el SaveChanges de la clase base.
/// </summary>
public sealed class HistorialClienteErpConsumidor : EventConsumerBase<CreditoDbContext, HistorialClienteErpActualizadoData>
{
    public const string NombreCola = "credito.erp";

    public HistorialClienteErpConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<HistorialClienteErpConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => NombreCola;

    protected override string NombreServicio => "credito";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, HistorialClienteErpActualizado.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<HistorialClienteErpActualizadoData> evento,
        CreditoDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var servicio = servicios.GetRequiredService<ServicioPerfilCrediticio>();
        await servicio.AplicarHistorialAsync(evento.Data, cancelacion, confirmar: false);
    }
}
