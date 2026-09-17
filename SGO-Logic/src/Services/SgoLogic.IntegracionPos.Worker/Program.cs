using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.IntegracionPos.Worker.Application.Pos;
using SgoLogic.IntegracionPos.Worker.Consumers;
using SgoLogic.IntegracionPos.Worker.Infrastructure.Persistencia;
using SgoLogic.IntegracionPos.Worker.Infrastructure.Pos;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddDbContext<IntegracionPosDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<IntegracionPosDbContext>(builder.Configuration);

builder.Services.Configure<PosOptions>(builder.Configuration.GetSection(PosOptions.Seccion));
builder.Services.AddHttpClient<IClientePos, ClientePosHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<Microsoft.Extensions.Options.IOptions<PosOptions>>().Value;
    http.BaseAddress = new Uri(opciones.BaseUrl);
    http.Timeout = TimeSpan.FromMilliseconds(opciones.TiempoEsperaMs);
});

builder.Services.AddSingleton<IServicioMapeoPos, ServicioMapeoPos>();
builder.Services.AddHostedService<PosEventosConsumidor>();

var app = builder.Build();

app.UseSgoDefaults();

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<IntegracionPosDbContext>();
    contexto.Database.Migrate();
}

app.Run();
