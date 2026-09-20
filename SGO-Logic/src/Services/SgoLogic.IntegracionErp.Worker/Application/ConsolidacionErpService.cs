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

        foreach (var venta in ventas)
            lote.Registros.Add(new RegistroLoteErp { Lote = lote, OrdenId = venta.OrdenId, Canal = venta.Canal, Tienda = venta.Tienda, Monto = venta.Monto, Fecha = venta.FechaVenta });

        var registros = lote.Registros.Select(x => new RegistroVentaErp(x.OrdenId, x.Canal, x.Tienda, x.Monto, x.Fecha)).ToList();
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
        foreach (var confirmacion in resumen.Lotes)
        {
            var lote = await _contexto.Lotes.FirstOrDefaultAsync(x => x.LoteId == confirmacion.LoteId, cancelacion);
            if (lote is null) continue;
            var conciliada = lote.CantidadRegistros == confirmacion.CantidadRegistros && Math.Abs(lote.MontoTotal - confirmacion.MontoTotal) <= 0.01m;
            _contexto.Conciliaciones.Add(new ConciliacionErp
            {
                Fecha = DateTime.UtcNow.Date, LoteId = lote.LoteId, RegistrosSgo = lote.CantidadRegistros, MontoSgo = lote.MontoTotal,
                RegistrosErp = confirmacion.CantidadRegistros, MontoErp = confirmacion.MontoTotal, Conciliada = conciliada,
                Diferencias = conciliada ? null : $"SGO={lote.CantidadRegistros}/{lote.MontoTotal:0.00}; ERP={confirmacion.CantidadRegistros}/{confirmacion.MontoTotal:0.00}"
            });
        }
        await _contexto.SaveChangesAsync(cancelacion);
    }
}
