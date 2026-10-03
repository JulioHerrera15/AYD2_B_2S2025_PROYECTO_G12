using System.Text.Json;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;

namespace SgoLogic.Credito.Api.Consumers;

/// <summary>
/// Consume la cola <c>credito.pagos</c>: el resultado del cobro de los abonos que
/// publica Pagos (<c>pagos.abono-credito.cobrado</c>, <c>.rechazado</c> y
/// <c>.pendiente</c>). Deduplica por eventId con EventConsumerBase y, ademas, el
/// servicio es idempotente por abonoId (un abono con resultado final no cambia).
///
/// Como en <c>credito.ordenes</c>, el servicio guarda por su cuenta (reintenta ante
/// choque de Version de la cuenta o la linea) y la marca de procesado se guarda
/// despues; una redelivery entre ambos pasos es inofensiva.
/// </summary>
public sealed class PagosCreditoConsumidor : EventConsumerBase<CreditoDbContext, JsonElement>
{
    public const string NombreCola = "credito.pagos";

    private readonly ILogger<PagosCreditoConsumidor> _logger;

    public PagosCreditoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<PagosCreditoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _logger = logger;
    }

    protected override string Cola => NombreCola;

    protected override string NombreServicio => "credito";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, AbonoCreditoCobrado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, AbonoCreditoRechazado.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, AbonoCreditoPendiente.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<JsonElement> evento,
        CreditoDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var servicio = servicios.GetRequiredService<ServicioResultadoAbonos>();
        switch (evento.EventType)
        {
            case AbonoCreditoCobrado.EventType:
                await servicio.CobradoAsync(Leer<AbonoCreditoCobradoData>(evento.Data), cancelacion);
                break;

            case AbonoCreditoRechazado.EventType:
                await servicio.RechazadoAsync(Leer<AbonoCreditoRechazadoData>(evento.Data), cancelacion);
                break;

            case AbonoCreditoPendiente.EventType:
                await servicio.PendienteAsync(Leer<AbonoCreditoPendienteData>(evento.Data), cancelacion);
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
