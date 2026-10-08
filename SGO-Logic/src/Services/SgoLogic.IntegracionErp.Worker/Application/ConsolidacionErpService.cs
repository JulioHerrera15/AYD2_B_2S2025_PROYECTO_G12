using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Worker.Application;

public sealed class ConsolidacionErpService
{
    private readonly IntegracionErpDbContext _contexto;
    private readonly IGeneradorLoteErp _generador;
    private readonly IClienteErp _cliente;
    private readonly IOutboxWriter _outbox;
    private readonly ErpOptions _opciones;
    private readonly ILogger<ConsolidacionErpService> _logger;

    public ConsolidacionErpService(IntegracionErpDbContext contexto, IGeneradorLoteErp generador, IClienteErp cliente, IOutboxWriter outbox, IOptions<ErpOptions> opciones, ILogger<ConsolidacionErpService> logger)
    {
        _contexto = contexto; _generador = generador; _cliente = cliente; _outbox = outbox; _opciones = opciones.Value; _logger = logger;
    }

    public async Task<LoteErp?> ConsolidarYTransferirAsync(CancellationToken cancelacion)
    {
        var lotePendiente = await _contexto.Lotes
            .Include(x => x.Registros)
            .Where(x => x.Estado == "PENDIENTE_REINTENTO")
            .OrderBy(x => x.Secuencia)
            .FirstOrDefaultAsync(cancelacion);

        if (lotePendiente is not null)
            return await TransferirLoteAsync(lotePendiente, cancelacion);

        var ventas = await _contexto.Ventas
            .Where(x => x.Estado == "PENDIENTE")
            .OrderBy(x => x.FechaVenta)
            .ToListAsync(cancelacion);
        if (ventas.Count == 0) return null;

        var secuencia = (await _contexto.Lotes.MaxAsync(x => (long?)x.Secuencia, cancelacion) ?? 0) + 1;
        var lote = new LoteErp
        {
            LoteId = $"LOTE-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{secuencia:D6}",
            Secuencia = secuencia,
            FechaGeneracion = DateTime.UtcNow,
            Formato = _opciones.Formato.ToUpperInvariant(),
            CantidadRegistros = ventas.Count,
            MontoTotal = ventas.Sum(x => x.Monto)
        };

        var registros = new List<RegistroVentaErp>(ventas.Count);
        foreach (var venta in ventas)
        {
            lote.Registros.Add(new RegistroLoteErp { Lote = lote, OrdenId = venta.OrdenId, Canal = venta.Canal, Tienda = venta.Tienda, Monto = venta.Monto, Fecha = venta.FechaVenta });
            registros.Add(new RegistroVentaErp(venta.OrdenId, venta.Canal, venta.Tienda, venta.Monto, venta.FechaVenta, venta.ClienteId));
        }

        lote.Contenido = _generador.Generar(new LoteParaEnviar(lote.LoteId, lote.Secuencia, lote.FechaGeneracion, registros), lote.Formato);
        lote.Estado = "ENVIANDO";
        lote.Intentos = 0;
        lote.EnviadoEn = DateTime.UtcNow;
        _contexto.Lotes.Add(lote);
        await _contexto.SaveChangesAsync(cancelacion);

        return await TransferirLoteAsync(lote, cancelacion);
    }

    private async Task<LoteErp> TransferirLoteAsync(LoteErp lote, CancellationToken cancelacion)
    {
        var ordenes = lote.Registros.Select(x => x.OrdenId).ToList();
        var ventas = await _contexto.Ventas.Where(x => ordenes.Contains(x.OrdenId)).ToListAsync(cancelacion);
        var respuesta = await _cliente.EnviarLoteAsync(lote.Contenido ?? string.Empty, _generador.ContentType(lote.Formato), cancelacion);

        lote.Intentos += 1;
        lote.CodigoHttp = respuesta.CodigoHttp;
        lote.RespuestaErp = respuesta.Raw;
        lote.ConfirmadoEn = respuesta.Exitoso ? DateTime.UtcNow : null;
        lote.Estado = respuesta.Exitoso
            ? (respuesta.Duplicado ? "DUPLICADO" : "ACEPTADO")
            : lote.Intentos >= Math.Max(1, _opciones.MaxIntentosLote) ? "FALLIDO" : "PENDIENTE_REINTENTO";

        if (respuesta.Exitoso)
            foreach (var venta in ventas) venta.Estado = "ENVIADA";

        _outbox.Encolar(
            LoteErpProcesado.EventType,
            LoteErpProcesado.AggregateType,
            lote.LoteId,
            lote.Intentos,
            TopologiaMensajeria.ExchangeEventos,
            LoteErpProcesado.RoutingKey,
            new LoteErpProcesadoData(lote.LoteId, lote.Secuencia, lote.Estado, lote.CantidadRegistros, lote.MontoTotal, respuesta.Detalle, DateTime.UtcNow));
        await _contexto.SaveChangesAsync(cancelacion);
        _logger.LogInformation("Lote ERP {LoteId} finalizado con estado {Estado}", lote.LoteId, lote.Estado);
        return lote;
    }

    public async Task ConciliarAsync(CancellationToken cancelacion)
    {
        var resumen = await _cliente.ObtenerResumenAsync(cancelacion);
        if (resumen is null) return;

        var fechaConciliacion = DateTime.UtcNow.Date;
        var confirmaciones = resumen.Lotes.ToDictionary(x => x.LoteId, StringComparer.Ordinal);
        var lotesLocales = await _contexto.Lotes
            .AsNoTracking()
            .Where(x => x.EnviadoEn != null && x.Estado != "GENERADO")
            .ToListAsync(cancelacion);

        foreach (var lote in lotesLocales)
        {
            confirmaciones.TryGetValue(lote.LoteId, out var confirmacion);
            var registrosErp = confirmacion?.CantidadRegistros ?? 0;
            var montoErp = confirmacion?.MontoTotal ?? 0m;
            var diferencias = new List<string>();

            if (confirmacion is null)
                diferencias.Add("No existe confirmacion para el lote enviado.");
            else
            {
                if (!string.Equals(confirmacion.Estado, "ACEPTADO", StringComparison.OrdinalIgnoreCase))
                    diferencias.Add($"Estado ERP={confirmacion.Estado}; se esperaba ACEPTADO.");
                if (lote.CantidadRegistros != registrosErp)
                    diferencias.Add($"Cantidad SGO={lote.CantidadRegistros}; ERP={registrosErp}.");
                if (Math.Abs(lote.MontoTotal - montoErp) > 0.01m)
                    diferencias.Add($"Monto SGO={lote.MontoTotal:0.00}; ERP={montoErp:0.00}.");
            }

            await GuardarConciliacionAsync(
                fechaConciliacion, lote.LoteId, lote.CantidadRegistros, lote.MontoTotal,
                registrosErp, montoErp, diferencias, cancelacion);
        }

        foreach (var confirmacion in resumen.Lotes.Where(x => !lotesLocales.Any(l => l.LoteId == x.LoteId)))
        {
            await GuardarConciliacionAsync(
                fechaConciliacion, confirmacion.LoteId, 0, 0m, confirmacion.CantidadRegistros,
                confirmacion.MontoTotal,
                [$"El ERP reporta un lote sin registro local; estado ERP={confirmacion.Estado}."],
                cancelacion);
        }

        await _contexto.SaveChangesAsync(cancelacion);
    }

    private async Task GuardarConciliacionAsync(
        DateTime fecha, string loteId, int registrosSgo, decimal montoSgo, int registrosErp,
        decimal montoErp, IReadOnlyCollection<string> diferencias, CancellationToken cancelacion)
    {
        var conciliacion = await _contexto.Conciliaciones
            .FirstOrDefaultAsync(x => x.Fecha == fecha && x.LoteId == loteId, cancelacion);

        if (conciliacion is null)
        {
            conciliacion = new ConciliacionErp { Fecha = fecha, LoteId = loteId };
            _contexto.Conciliaciones.Add(conciliacion);
        }

        conciliacion.RegistrosSgo = registrosSgo;
        conciliacion.MontoSgo = montoSgo;
        conciliacion.RegistrosErp = registrosErp;
        conciliacion.MontoErp = montoErp;
        conciliacion.Conciliada = diferencias.Count == 0;
        conciliacion.Diferencias = conciliacion.Conciliada ? null : string.Join(" ", diferencias);
        conciliacion.CreadaEn = DateTime.UtcNow;
    }
}
