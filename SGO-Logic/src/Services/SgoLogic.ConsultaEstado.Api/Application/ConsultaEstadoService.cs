using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SgoLogic.ConsultaEstado.Api.Domain;
using SgoLogic.ConsultaEstado.Api.Infrastructure;
using StackExchange.Redis;

namespace SgoLogic.ConsultaEstado.Api.Application;

public interface IConsultaEstadoService
{
    Task<OrdenEstadoDto?> ObtenerOrdenAsync(string ordenId, CancellationToken ct = default);
    Task<HistorialOrdenesDto> ObtenerHistorialAsync(string clienteId, int pagina, int tamano, CancellationToken ct = default);
}

public sealed class ConsultaEstadoService : IConsultaEstadoService
{
    private readonly ConsultaEstadoDbContext _contexto;
    private readonly IDatabase _redis;
    private readonly ILogger<ConsultaEstadoService> _logger;

    public ConsultaEstadoService(
        ConsultaEstadoDbContext contexto, 
        IConnectionMultiplexer redis,
        ILogger<ConsultaEstadoService> logger)
    {
        _contexto = contexto;
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<OrdenEstadoDto?> ObtenerOrdenAsync(string ordenId, CancellationToken ct = default)
    {
        var cronometro = System.Diagnostics.Stopwatch.StartNew();

        // 1. Cache caliente en Redis con tolerancia a caidas
        RedisValue json = RedisValue.Null;
        try
        {
            json = await _redis.StringGetAsync($"orden:{ordenId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis no disponible para orden {OrdenId}. Procediendo con fallback frio a PostgreSQL.", ordenId);
        }

        OrdenVista? vista = null;
        if (!json.IsNullOrEmpty)
        {
            vista = JsonSerializer.Deserialize<OrdenVista>(json.ToString());
        }

        // 2. Fallback frio en PostgreSQL
        if (vista is null)
        {
            vista = await _contexto.OrdenesVista.FindAsync(new object[] { ordenId }, ct);
            if (vista is null)
            {
                var incidenciasPrevias = await _contexto.Incidencias
                    .Where(i => i.OrdenId == ordenId)
                    .OrderByDescending(i => i.CreadaEn)
                    .ToListAsync(ct);

                if (incidenciasPrevias.Count > 0)
                {
                    vista = new OrdenVista
                    {
                        OrdenId = ordenId,
                        NumeroOrden = ordenId,
                        Estado = "REGISTRADA",
                        Canal = "WEB",
                        MetodoEntrega = "DOMICILIO",
                        Moneda = "GTQ",
                        CreadaEn = incidenciasPrevias.Last().CreadaEn,
                        ActualizadaEn = incidenciasPrevias.First().CreadaEn
                    };
                    return MapearVista(vista, incidenciasPrevias);
                }

                return null;
            }

            // Re-hidratar Redis de forma controlada
            try
            {
                await _redis.StringSetAsync($"orden:{ordenId}", JsonSerializer.Serialize(vista), TimeSpan.FromHours(24));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo rehidratar cache Redis para orden {OrdenId}.", ordenId);
            }
        }

        // 3. Cargar incidencias siempre desde PostgreSQL (fuente de verdad para incidencias)
        var incidencias = await _contexto.Incidencias
            .Where(i => i.OrdenId == ordenId)
            .OrderByDescending(i => i.CreadaEn)
            .ToListAsync(ct);

        cronometro.Stop();
        if (cronometro.ElapsedMilliseconds > 5000)
        {
            _logger.LogError("ALERTA EAC-02: Latencia de consulta para orden {OrdenId} excedio el limite ({Tiempo}ms > 5000ms)", ordenId, cronometro.ElapsedMilliseconds);
        }

        return MapearVista(vista, incidencias);
    }

    public async Task<HistorialOrdenesDto> ObtenerHistorialAsync(
        string clienteId, int pagina, int tamano, CancellationToken ct = default)
    {
        tamano = Math.Clamp(tamano, 1, 100);
        pagina = Math.Max(1, pagina);

        var total = await _contexto.OrdenesVista
            .CountAsync(o => o.ClienteId == clienteId, ct);

        var ordenes = await _contexto.OrdenesVista
            .Where(o => o.ClienteId == clienteId)
            .OrderByDescending(o => o.ActualizadaEn)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        return new HistorialOrdenesDto(
            ClienteId: clienteId,
            Pagina: pagina,
            Tamano: tamano,
            TotalRegistros: total,
            Ordenes: ordenes.Select(o => new OrdenResumenDto(
                o.OrdenId, o.NumeroOrden ?? string.Empty, o.Estado ?? "CREADA", o.Total, o.ActualizadaEn))
                .ToList());
    }

    private static OrdenEstadoDto MapearVista(OrdenVista v, List<Incidencia> incidencias) =>
        new(
            OrdenId: v.OrdenId,
            NumeroOrden: v.NumeroOrden ?? string.Empty,
            ClienteId: v.ClienteId ?? string.Empty,
            Canal: v.Canal ?? "WEB",
            Estado: v.Estado ?? "CREADA",
            EstadoAnterior: v.EstadoAnterior,
            MetodoEntrega: v.MetodoEntrega ?? "DOMICILIO",
            Moneda: v.Moneda ?? "GTQ",
            Total: v.Total,
            PagoId: v.PagoId,
            TiendaDestinoId: v.TiendaDestinoId,
            DocumentoFiscalId: v.DocumentoFiscalId,
            MotivoUltimoCambio: v.MotivoUltimoCambio,
            ActualizadaEn: v.ActualizadaEn,
            Incidencias: incidencias.Select(i => new IncidenciaDto(
                i.Id, i.Estado, i.Descripcion, i.Resolucion, i.CreadaEn, i.ActualizadaEn))
                .ToList());
}
