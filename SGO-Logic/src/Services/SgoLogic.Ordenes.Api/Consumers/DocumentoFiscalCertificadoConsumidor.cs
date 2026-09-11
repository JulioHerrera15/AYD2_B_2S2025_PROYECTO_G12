using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Infrastructure.Persistencia;

namespace SgoLogic.Ordenes.Api.Consumers;

/// <summary>
/// Consume <c>facturacion.documento-fiscal.certificado</c> (evento 1 de
/// facturacion-eventos-v1). Cierra el tramo CONFIRMADA_PENDIENTE_FISCAL →
/// CONFIRMADA de la saga de Órdenes: persiste los datos fiscales en la orden,
/// aplica la transición y emite <c>OrdenCertificadaFiscalmente</c> (evento 12)
/// por outbox.
///
/// Usa <c>EventConsumerBase</c> (mismo patrón que
/// <see cref="SgoLogic.Facturacion.Api.Consumers.CertificacionFiscalSolicitadaConsumidor" />):
/// la clase base aporta deduplicación por <c>eventId</c>, correlación y
/// ack/nack; el manejador no llama a <c>SaveChangesAsync</c>, la clase base
/// guarda la marca de procesado en la misma transacción que el cambio de negocio.
/// </summary>
public sealed class DocumentoFiscalCertificadoConsumidor
    : EventConsumerBase<OrdenesDbContext, DocumentoFiscalCertificadoData>
{
    public DocumentoFiscalCertificadoConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        ILogger<DocumentoFiscalCertificadoConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
    }

    protected override string Cola => "ordenes.facturacion";

    protected override string NombreServicio => "ordenes";

    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, DocumentoFiscalCertificado.RoutingKey)
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<DocumentoFiscalCertificadoData> evento,
        OrdenesDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        var manejador = servicios.GetRequiredService<ManejadorDocumentoFiscalCertificado>();

        await manejador.ManejarAsync(evento.Data, cancelacion);
    }
}
