using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.IntegracionErp.Worker.Application;
using SgoLogic.IntegracionErp.Worker.Consumers;
using SgoLogic.IntegracionErp.Worker.Infrastructure;
using SgoLogic.IntegracionErp.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.AddSgoDefaults();
builder.Services.AddDbContext<IntegracionErpDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<IErpBatchLock>(_ => new PostgresErpBatchLock(builder.Configuration.GetConnectionString("Postgres")!));
builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<IntegracionErpDbContext>(builder.Configuration);
builder.Services.Configure<ErpOptions>(builder.Configuration.GetSection(ErpOptions.Seccion));
builder.Services.AddHttpClient<IClienteErp, ClienteErpHttp>((sp, http) => { var options = sp.GetRequiredService<IOptions<ErpOptions>>().Value; http.BaseAddress = new Uri(options.BaseUrl); http.Timeout = TimeSpan.FromMilliseconds(options.TiempoEsperaMs); });
builder.Services.AddSingleton<IGeneradorLoteErp, GeneradorLoteErp>();
builder.Services.AddScoped<ConsolidacionErpService>();
builder.Services.AddHostedService<ErpOrdenesConsumidor>();
builder.Services.AddHostedService<ErpBatchWorker>();
builder.Services.AddHostedService<ErpConciliacionWorker>();
var app = builder.Build();
app.UseSgoDefaults();

app.MapGet("/api/erp/conciliaciones", async (
	IntegracionErpDbContext contexto,
	DateTime? desde,
	CancellationToken cancelacion,
	bool soloDiferencias = false) =>
{
	var consulta = contexto.Conciliaciones.AsNoTracking();
	if (desde.HasValue)
		consulta = consulta.Where(x => x.Fecha >= desde.Value.Date);
	if (soloDiferencias)
		consulta = consulta.Where(x => !x.Conciliada);

	var resultados = await consulta
		.OrderByDescending(x => x.Fecha)
		.ThenByDescending(x => x.CreadaEn)
		.Take(1000)
		.ToListAsync(cancelacion);

	return Results.Ok(resultados);
});

// Informe de ventas conciliado: cada lote con sus ordenes (trazabilidad) y su
// ultima conciliacion contra el ERP, mas las ventas que aun no salen en un lote.
app.MapGet("/api/erp/informe", async (
	IntegracionErpDbContext contexto,
	DateTime? desde,
	DateTime? hasta,
	CancellationToken cancelacion) =>
{
	var consulta = contexto.Lotes.AsNoTracking().Include(x => x.Registros).AsQueryable();
	if (desde.HasValue) consulta = consulta.Where(x => x.FechaGeneracion >= desde.Value.ToUniversalTime());
	if (hasta.HasValue) consulta = consulta.Where(x => x.FechaGeneracion < hasta.Value.ToUniversalTime().AddDays(1));

	var lotes = await consulta.OrderByDescending(x => x.Secuencia).Take(500).ToListAsync(cancelacion);
	var loteIds = lotes.Select(x => x.LoteId).ToList();
	var conciliaciones = (await contexto.Conciliaciones.AsNoTracking()
			.Where(x => loteIds.Contains(x.LoteId))
			.ToListAsync(cancelacion))
		.GroupBy(x => x.LoteId)
		.ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreadaEn).First());

	var pendientes = await contexto.Ventas.AsNoTracking().Where(x => x.Estado == "PENDIENTE").ToListAsync(cancelacion);

	return Results.Ok(new
	{
		generadoEn = DateTime.UtcNow,
		ventasPendientes = pendientes.Count,
		montoPendiente = pendientes.Sum(x => x.Monto),
		lotes = lotes.Select(l =>
		{
			conciliaciones.TryGetValue(l.LoteId, out var c);
			return new
			{
				l.LoteId, l.Secuencia, l.FechaGeneracion, l.Formato, l.Estado, l.CantidadRegistros, l.MontoTotal,
				l.Intentos, l.CodigoHttp, l.EnviadoEn, l.ConfirmadoEn,
				conciliacion = c is null ? null : new { c.Fecha, c.RegistrosErp, c.MontoErp, c.Conciliada, c.Diferencias, c.CreadaEn },
				registros = l.Registros.OrderBy(r => r.Fecha).Select(r => new { r.OrdenId, r.Canal, r.Tienda, r.Monto, r.Fecha })
			};
		})
	});
});

// Ejecuta consolidacion + conciliacion bajo demanda (el temporizador corre cada 24 h).
app.MapPost("/api/erp/conciliar", async (
	IErpBatchLock batchLock,
	ConsolidacionErpService servicio,
	CancellationToken cancelacion) =>
{
	await using var lease = await batchLock.TryAcquireAsync(cancelacion);
	if (lease is null) return Results.Conflict(new { mensaje = "Ya hay una operacion ERP en curso; intenta en unos segundos." });

	await servicio.ConsolidarYTransferirAsync(cancelacion);
	await servicio.ConciliarAsync(cancelacion);
	return Results.Ok(new { mensaje = "Consolidacion y conciliacion ejecutadas." });
});

app.MapGet("/api/erp/lotes/fallidos", async (
	IntegracionErpDbContext contexto,
	CancellationToken cancelacion) =>
{
	var lotes = await contexto.Lotes
		.AsNoTracking()
		.Where(x => x.Estado == "FALLIDO")
		.OrderByDescending(x => x.FechaGeneracion)
		.Take(1000)
		.ToListAsync(cancelacion);

	return Results.Ok(lotes);
});

app.MapPost("/api/erp/lotes/{loteId}/reintentar", async (
	string loteId,
	IntegracionErpDbContext contexto,
	CancellationToken cancelacion) =>
{
	var lote = await contexto.Lotes.FirstOrDefaultAsync(x => x.LoteId == loteId, cancelacion);
	if (lote is null) return Results.NotFound();

	if (lote.Estado != "FALLIDO")
		return Results.Conflict(new { mensaje = "Solo se pueden reintentar lotes en estado FALLIDO." });

	lote.Estado = "PENDIENTE_REINTENTO";
	lote.Intentos = 0;
	await contexto.SaveChangesAsync(cancelacion);
	return Results.Accepted($"/api/erp/lotes/{loteId}", new { loteId, estado = lote.Estado });
});

using (var scope = app.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<IntegracionErpDbContext>().Database.Migrate();
app.Run();
