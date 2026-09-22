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
	bool soloDiferencias,
	CancellationToken cancelacion) =>
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
