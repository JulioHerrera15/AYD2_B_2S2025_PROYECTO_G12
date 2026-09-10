using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Consumers;

/// <summary>
/// Consume <c>ordenes.cobro.solicitado</c> (CU-03). Delega el nucleo de HU-004 en
/// <see cref="ServicioCobro" />, que registra el intento antes de invocar a
/// PayKlik y encola el evento de resultado. No llama a SaveChangesAsync:
/// EventConsumerBase guarda el intento, el pago, el enlace, la fila de outbox y la
/// marca de mensaje procesado en una sola transaccion.
///
/// Una falla de PayKlik no nackea el mensaje: es un resultado de negocio
/// (<c>ServicioCobro</c> lo traduce a pago PENDIENTE). Solo nackea ante un error
/// tecnico del propio lado, y de eso se encarga la clase base al fallar su
/// SaveChangesAsync.
/// </summary>
public sealed class CobroSolicitadoConsumidor : EventConsumerBase<PagosDbContext, CobroSolicitadoData>
{
    public CobroSolicitadoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<CobroSolicitadoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "pagos.ordenes";

    protected override string NombreServicio => "pagos";

    // ordenes-eventos-v1.md (seccion 4) liga esta cola tambien a
    // ordenes.devolucion.registrada. Ese evento aun no existe y EventConsumerBase
    // esta tipado a un solo TData, asi que hoy se declara solo el binding de cobro.
    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "ordenes.cobro.solicitado")
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<CobroSolicitadoData> evento,
        PagosDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var servicio = servicios.GetRequiredService<ServicioCobro>();
        var datos = evento.Data;

        var solicitud = new SolicitudCobro(
            datos.OrdenId,
            datos.ClienteId,
            datos.Canal,
            datos.Moneda,
            datos.Monto,
            datos.MedioPago,
            datos.TokenMedioPago,
            datos.UltimosCuatro,
            datos.Marca);

        await servicio.ProcesarCobroAsync(solicitud, cancelacion);
    }
}
