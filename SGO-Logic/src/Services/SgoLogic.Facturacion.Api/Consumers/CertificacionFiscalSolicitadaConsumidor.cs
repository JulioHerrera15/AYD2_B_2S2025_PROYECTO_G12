using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

namespace SgoLogic.Facturacion.Api.Consumers;

/// <summary>
/// Consume <c>ordenes.fiscal.certificacion-solicitada</c> (evento 6 de
/// ordenes-eventos-v1, CU-04). Es el disparador de la facturacion: genera el
/// documento fiscal con su XML, serie y correlativo, y lo persiste. El envio a la
/// SAT no ocurre aca (lo encola el worker de reintentos), asi que consumir este
/// evento no bloquea el flujo de la orden.
///
/// <c>EventConsumerBase</c> aporta la deduplicacion por <c>eventId</c>, la
/// correlacion y el ack/nack; <see cref="ServicioFacturacion" /> no llama a
/// SaveChangesAsync, la clase base guarda el documento y la marca de procesado en
/// una sola transaccion.
///
/// Pendiente explicito: ordenes-eventos-v1 seccion 4 liga la cola
/// <c>facturacion.ordenes</c> tambien a <c>ordenes.devolucion.registrada</c> (nota
/// de credito, HU-014). Ese evento aun no existe; el binding se agregara entonces.
/// </summary>
public sealed class CertificacionFiscalSolicitadaConsumidor
    : EventConsumerBase<FacturacionDbContext, CertificacionFiscalSolicitadaData>
{
    public CertificacionFiscalSolicitadaConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<CertificacionFiscalSolicitadaConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "facturacion.ordenes";

    protected override string NombreServicio => "facturacion";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, CertificacionFiscalSolicitada.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<CertificacionFiscalSolicitadaData> evento,
        FacturacionDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var servicio = servicios.GetRequiredService<ServicioFacturacion>();

        await servicio.CrearDocumentoAsync(
            evento.Data,
            evento.TransactionId,
            DateTime.UtcNow,
            cancelacion);
    }
}
