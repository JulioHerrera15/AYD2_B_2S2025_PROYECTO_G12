using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Consumers;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<PagosDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<PagosDbContext>(builder.Configuration);

builder.Services.Configure<PayKlikOptions>(builder.Configuration.GetSection(PayKlikOptions.Seccion));
builder.Services.Configure<ReintentoOptions>(builder.Configuration.GetSection(ReintentoOptions.Seccion));

// El HttpClient tipado invoca a PayKlik "a pelo"; el circuit breaker (ADR-009) lo
// envuelve con ClientePayKlikConDisyuntor, que es a quien resuelve IClientePayKlik.
builder.Services.AddHttpClient<ClientePayKlikHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<PayKlikOptions>>().Value;
    http.BaseAddress = new Uri(opciones.BaseUrl);
    http.Timeout = TimeSpan.FromMilliseconds(opciones.TimeoutMs);
});

builder.Services.AddSingleton<DisyuntorPayKlik>();
builder.Services.AddScoped<IClientePayKlik>(proveedor => new ClientePayKlikConDisyuntor(
    proveedor.GetRequiredService<ClientePayKlikHttp>(),
    proveedor.GetRequiredService<DisyuntorPayKlik>()));

builder.Services.AddScoped<ServicioCobro>();
builder.Services.AddScoped<ServicioReembolso>();
builder.Services.AddHostedService<PagosEventosConsumidor>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<PagosDbContext>();
    contexto.Database.Migrate();
}

app.Run();
