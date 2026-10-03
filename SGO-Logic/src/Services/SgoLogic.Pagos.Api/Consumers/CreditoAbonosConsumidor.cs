using System.Text.Json;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Consumers;

/// <summary>
/// Consume la cola <c>pagos.credito</c> (bindings a <c>credito.abono.cobro-solicitado</c>
/// y <c>credito.abono.reembolso-solicitado</c>). El cobro lo hace
/// <see cref="ServicioCobro.ProcesarCobroAbonoAsync" />; el reembolso lo registra
/// PENDIENTE <see cref="ServicioReembolsosPendientes.CrearPorAbonoAsync" /> y lo envia el
/// worker. Al llegar dos tipos de data, la clase base se tipa a <see cref="JsonElement" />
/// y el despacho se hace por eventType; deduplicacion por eventId y un solo SaveChanges
/// con la marca de procesado, igual que antes.
/// </summary>
public sealed class CreditoAbonosConsumidor : EventConsumerBase<PagosDbContext, JsonElement>
{
    public const string NombreCola = "pagos.credito";

    private readonly ILogger<CreditoAbonosConsumidor> _logger;

    public CreditoAbonosConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<CreditoAbonosConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _logger = logger;
    }

    protected override string Cola => NombreCola;

    protected override string NombreServicio => "pagos";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CobroAbonoCreditoSolicitado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, ReembolsoAbonoCreditoSolicitado.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<JsonElement> evento,
        PagosDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        switch (evento.EventType)
        {
            case CobroAbonoCreditoSolicitado.EventType:
                await servicios.GetRequiredService<ServicioCobro>()
                    .ProcesarCobroAbonoAsync(Leer<CobroAbonoCreditoSolicitadoData>(evento.Data), cancelacion);
                break;

            case ReembolsoAbonoCreditoSolicitado.EventType:
                await servicios.GetRequiredService<ServicioReembolsosPendientes>()
                    .CrearPorAbonoAsync(Leer<ReembolsoAbonoCreditoSolicitadoData>(evento.Data), cancelacion);
                break;

            default:
                _logger.LogWarning(
                    "Tipo de evento {EventType} no esperado en {Cola}; se marca procesado sin aplicar.",
                    evento.EventType, NombreCola);
                break;
        }
    }

    private static TData Leer<TData>(JsonElement data)
        where TData : notnull =>
        data.Deserialize<TData>(SgoJsonSerializerOptions.Default)
        ?? throw new JsonException($"El campo data no coincide con {typeof(TData).Name}.");
}
