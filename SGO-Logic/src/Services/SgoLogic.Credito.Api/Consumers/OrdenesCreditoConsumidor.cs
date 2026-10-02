using System.Text.Json;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Infrastructure.Persistencia;

namespace SgoLogic.Credito.Api.Consumers;

/// <summary>
/// Consume la cola <c>credito.ordenes</c> con bindings a
/// <c>ordenes.credito.evaluacion-solicitada</c> y <c>ordenes.orden.cancelada</c>.
/// Al llegar dos tipos de data, la clase base se tipa a <see cref="JsonElement" /> y
/// el despacho se hace por eventType; la deduplicacion por eventId sigue siendo la
/// de EventConsumerBase.
///
/// A diferencia del consumidor de historiales, aqui los servicios guardan por su
/// cuenta: necesitan releer y reintentar ante un choque de Version de la linea, y
/// eso exige confirmar dentro del propio servicio. La marca de procesado se guarda
/// despues, en el SaveChanges de la clase base. Si el proceso cae entre ambos, la
/// redelivery es inofensiva: la decision es unica por orden y anular una cuenta ya
/// anulada no hace nada.
/// </summary>
public sealed class OrdenesCreditoConsumidor : EventConsumerBase<CreditoDbContext, JsonElement>
{
    public const string NombreCola = "credito.ordenes";

    private readonly ILogger<OrdenesCreditoConsumidor> _logger;

    public OrdenesCreditoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<OrdenesCreditoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _logger = logger;
    }

    protected override string Cola => NombreCola;

    protected override string NombreServicio => "credito";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, EvaluacionCreditoSolicitada.RoutingKey),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, OrdenCancelada.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<JsonElement> evento,
        CreditoDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        switch (evento.EventType)
        {
            case EvaluacionCreditoSolicitada.EventType:
                await servicios.GetRequiredService<ServicioEvaluacionCredito>()
                    .EvaluarAsync(Leer<EvaluacionCreditoSolicitadaData>(evento.Data), cancelacion);
                break;

            case OrdenCancelada.EventType:
                await servicios.GetRequiredService<ServicioAnulacionCargos>()
                    .ProcesarCancelacionAsync(Leer<OrdenCanceladaData>(evento.Data), cancelacion);
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
