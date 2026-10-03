using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Consumers;

/// <summary>
/// Consume la cola <c>pagos.credito</c> (binding a <c>credito.abono.cobro-solicitado</c>)
/// y cobra el abono con <see cref="ServicioCobro.ProcesarCobroAbonoAsync" />. Un solo
/// tipo de data, asi que usa EventConsumerBase: deduplicacion por eventId y el pago,
/// sus intentos, el resultado en el outbox y la marca de procesado se guardan en un
/// solo SaveChanges.
/// </summary>
public sealed class CreditoAbonosConsumidor : EventConsumerBase<PagosDbContext, CobroAbonoCreditoSolicitadoData>
{
    public const string NombreCola = "pagos.credito";

    public CreditoAbonosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<CreditoAbonosConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => NombreCola;

    protected override string NombreServicio => "pagos";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CobroAbonoCreditoSolicitado.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<CobroAbonoCreditoSolicitadoData> evento,
        PagosDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion) =>
        await servicios.GetRequiredService<ServicioCobro>().ProcesarCobroAbonoAsync(evento.Data, cancelacion);
}
