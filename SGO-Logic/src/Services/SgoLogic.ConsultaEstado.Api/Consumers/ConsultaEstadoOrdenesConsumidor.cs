using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Idempotencia;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;
using StackExchange.Redis;

namespace SgoLogic.ConsultaEstado.Api.Consumers;

/// <summary>
/// Consumidor de la cola consulta-estado.ordenes.
/// Recibe todos los eventos del dominio de Ordenes via binding ordenes.orden.*
/// y ordenes.devolucion.* y mantiene la vista materializada en Redis (caliente)
/// y en PostgreSQL (fallback / historial).
///
/// REGLA DE FUENTE AUTORITATIVA (sección 4.2 del instrucciones.txt):
///   Solo eventos 1 (OrdenCreada) y 2 (OrdenEstadoCambiado) escriben el campo Estado.
///   Los eventos 8, 9, 10, 11 y 12 enriquecen OTROS campos sin tocar Estado.
///
/// REGLA DE ORDENAMIENTO: se descarta cualquier evento cuyo Sequence sea menor o igual
/// al UltimoSequence registrado para ese AggregateId. Nunca ordenamos por OccurredAt.
/// </summary>
public sealed class ConsultaEstadoOrdenesConsumidor
    : EventConsumerBase<ConsultaEstadoDbContext, JsonElement>
{
    private readonly IDatabase _redis;
    private readonly ILogger<ConsultaEstadoOrdenesConsumidor> _logger;

    public ConsultaEstadoOrdenesConsumidor(
        IServiceScopeFactory scopeFactory,
        IConexionRabbitMq conexion,
        ISuscriptorMensajes suscriptor,
        IConnectionMultiplexer redis,
        ILogger<ConsultaEstadoOrdenesConsumidor> logger)
        : base(scopeFactory, conexion, suscriptor, logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    protected override string Cola => "consulta-estado.ordenes";
    protected override string NombreServicio => "consulta-estado";
    protected override IReadOnlyCollection<BindingCola> Bindings { get; } =
    [
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "ordenes.orden.*"),
        new BindingCola(TopologiaMensajeria.ExchangeEventos, "ordenes.devolucion.*")
    ];

    protected override async Task ManejarAsync(
        IntegrationEvent<JsonElement> evento,
        ConsultaEstadoDbContext contexto,
        IServiceProvider servicios,
        CancellationToken cancelacion)
    {
        // Dispatch por eventType — no usamos polimorfismo en el tipo genérico para poder
        // recibir todos los tipos de eventos desde la misma cola sin duplicar consumidores
        await (evento.EventType switch
        {
            OrdenCreada.EventType       => ManejarOrdenCreada(evento, contexto, cancelacion),
            OrdenEstadoCambiado.EventType => ManejarOrdenEstadoCambiado(evento, contexto, cancelacion),
            OrdenConfirmada.EventType   => ManejarOrdenConfirmada(evento, contexto, cancelacion),
            OrdenCancelada.EventType    => ManejarOrdenCancelada(evento, contexto, cancelacion),
            OrdenCertificadaFiscalmente.EventType => ManejarOrdenCertificadaFiscalmente(evento, contexto, cancelacion),
            // Eventos que no impactan la vista de estado se ackean sin procesar
            _ => Task.CompletedTask
        });
    }

    // ── Helpers de aplicacion de eventos ────────────────────────────────────────

    private async Task ManejarOrdenCreada(
        IntegrationEvent<JsonElement> evt,
        ConsultaEstadoDbContext ctx,
        CancellationToken ct)
    {
        var data = Deserializar<OrdenCreadaData>(evt.Data);
        if (!ValidarSequence(evt, ctx, out var vista)) return;

        if (vista is null)
        {
            vista = new OrdenVista
            {
                OrdenId = evt.AggregateId,
                NumeroOrden = data.NumeroOrden,
                ClienteId = data.ClienteId,
                Canal = data.Canal.ToString(),
                MetodoEntrega = data.MetodoEntrega.ToString(),
                UbicacionSolicitadaId = data.UbicacionSolicitadaId,
                Moneda = data.Moneda.ToString(),
                Subtotal = data.Subtotal,
                Impuestos = data.Impuestos,
                Total = data.Total,
                CreadaEn = evt.OccurredAt
            };
            ctx.OrdenesVista.Add(vista);
        }

        // EVENTO 1: escribe Estado
        vista.Estado = data.Estado.ToString();
        vista.UltimoSequence = evt.Sequence;
        vista.ActualizadaEn = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ct);
        await ActualizarRedis(vista, ct);
    }

    private async Task ManejarOrdenEstadoCambiado(
        IntegrationEvent<JsonElement> evt,
        ConsultaEstadoDbContext ctx,
        CancellationToken ct)
    {
        var data = Deserializar<OrdenEstadoCambiadoData>(evt.Data);
        var vista = await ObtenerOCrearVista(evt.AggregateId, ctx, ct);
        if (!AplicarSequence(evt, vista)) return;

        // EVENTO 2: escribe Estado (unica fuente autoritativa junto con evento 1)
        vista.Estado = data.EstadoNuevo.ToString();
        vista.EstadoAnterior = data.EstadoAnterior.ToString();
        vista.MotivoUltimoCambio = data.Motivo;
        vista.UltimoSequence = evt.Sequence;
        vista.ActualizadaEn = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ct);
        await ActualizarRedis(vista, ct);
    }

    private async Task ManejarOrdenConfirmada(
        IntegrationEvent<JsonElement> evt,
        ConsultaEstadoDbContext ctx,
        CancellationToken ct)
    {
        var data = Deserializar<OrdenConfirmadaData>(evt.Data);
        var vista = await ObtenerOCrearVista(evt.AggregateId, ctx, ct);
        if (!AplicarSequence(evt, vista)) return;

        // EVENTO 8: enriquece — NO toca Estado
        vista.PagoId = data.PagoId;
        vista.TiendaDestinoId = data.TiendaDestinoId;
        vista.UltimoSequence = evt.Sequence;
        vista.ActualizadaEn = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ct);
        await ActualizarRedis(vista, ct);
    }

    private async Task ManejarOrdenCancelada(
        IntegrationEvent<JsonElement> evt,
        ConsultaEstadoDbContext ctx,
        CancellationToken ct)
    {
        var data = Deserializar<OrdenCanceladaData>(evt.Data);
        var vista = await ObtenerOCrearVista(evt.AggregateId, ctx, ct);
        if (!AplicarSequence(evt, vista)) return;

        // EVENTO 9: enriquece — NO toca Estado
        vista.MotivoUltimoCambio = data.Motivo.ToString();
        vista.RequiereReembolso = data.RequiereReembolso;
        vista.MontoReembolso = data.MontoReembolso;
        vista.UltimoSequence = evt.Sequence;
        vista.ActualizadaEn = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ct);
        await ActualizarRedis(vista, ct);
    }

    private async Task ManejarOrdenCertificadaFiscalmente(
        IntegrationEvent<JsonElement> evt,
        ConsultaEstadoDbContext ctx,
        CancellationToken ct)
    {
        var data = Deserializar<OrdenCertificadaFiscalmenteData>(evt.Data);
        var vista = await ObtenerOCrearVista(evt.AggregateId, ctx, ct);
        if (!AplicarSequence(evt, vista)) return;

        // EVENTO 12: enriquece con datos fiscales — NO toca Estado
        vista.DocumentoFiscalId = data.DocumentoFiscalId;
        vista.SerieDocumentoFiscal = data.Serie;
        vista.NumeroDocumentoFiscal = data.NumeroDocumento;
        vista.UltimoSequence = evt.Sequence;
        vista.ActualizadaEn = DateTime.UtcNow;

        await ctx.SaveChangesAsync(ct);
        await ActualizarRedis(vista, ct);
    }

    // ── Helpers privados ────────────────────────────────────────────────────────

    private static TData Deserializar<TData>(JsonElement element)
        => element.Deserialize<TData>(SgoJsonSerializerOptions.Default)
           ?? throw new InvalidOperationException($"No se pudo deserializar como {typeof(TData).Name}");

    private bool ValidarSequence(
        IntegrationEvent<JsonElement> evt,
        ConsultaEstadoDbContext ctx,
        out OrdenVista? vista)
    {
        vista = ctx.OrdenesVista.Local.FirstOrDefault(o => o.OrdenId == evt.AggregateId);
        if (vista is not null && evt.Sequence <= vista.UltimoSequence)
        {
            _logger.LogDebug("Evento {EventId} descartado: sequence {Seq} <= ultimoSequence {Ultimo} para orden {OrdenId}",
                evt.EventId, evt.Sequence, vista.UltimoSequence, evt.AggregateId);
            return false;
        }
        return true;
    }

    private bool AplicarSequence(IntegrationEvent<JsonElement> evt, OrdenVista vista)
    {
        if (evt.Sequence <= vista.UltimoSequence)
        {
            _logger.LogDebug("Evento {EventId} descartado: sequence {Seq} <= ultimoSequence {Ultimo} para orden {OrdenId}",
                evt.EventId, evt.Sequence, vista.UltimoSequence, evt.AggregateId);
            return false;
        }
        return true;
    }

    private async Task<OrdenVista> ObtenerOCrearVista(
        string ordenId,
        ConsultaEstadoDbContext ctx,
        CancellationToken ct)
    {
        var vista = await ctx.OrdenesVista.FindAsync(new object[] { ordenId }, ct);
        if (vista is null)
        {
            // Orden llegó en orden no determinístico; crear placeholder para enriquecer
            vista = new OrdenVista
            {
                OrdenId = ordenId,
                CreadaEn = DateTime.UtcNow,
                ActualizadaEn = DateTime.UtcNow
            };
            ctx.OrdenesVista.Add(vista);
        }
        return vista;
    }

    private async Task ActualizarRedis(OrdenVista vista, CancellationToken ct)
    {
        var clave = $"orden:{vista.OrdenId}";
        var json = JsonSerializer.Serialize(vista);
        // TTL de 24 horas en Redis; PostgreSQL es el fallback durable
        await _redis.StringSetAsync(clave, json, TimeSpan.FromHours(24));
    }
}
