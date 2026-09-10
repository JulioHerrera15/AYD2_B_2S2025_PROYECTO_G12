using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Consumers;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;
using SgoLogic.Facturacion.Api.Infrastructure.Sat;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<FacturacionDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<FacturacionDbContext>(builder.Configuration);

builder.Services.Configure<SatOptions>(builder.Configuration.GetSection(SatOptions.Seccion));

builder.Services.AddHttpClient<ClienteSatHttp>((proveedor, http) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<SatOptions>>().Value;
    http.BaseAddress = new Uri(opciones.BaseUrl);
    http.Timeout = opciones.Timeout;
});
builder.Services.AddScoped<IClienteSat>(proveedor => proveedor.GetRequiredService<ClienteSatHttp>());

builder.Services.AddSingleton<GeneradorXmlFiscal>();
builder.Services.AddScoped<IGeneradorCorrelativoFiscal, GeneradorCorrelativoFiscal>();
builder.Services.AddScoped<ServicioFacturacion>();

builder.Services.AddHostedService<CertificacionFiscalSolicitadaConsumidor>();
builder.Services.AddHostedService<EnvioSatConsumidor>();
builder.Services.AddHostedService<WorkerReintentosSat>();

var app = builder.Build();

app.UseSgoDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var facturacion = app.MapGroup("/api/facturacion");

facturacion.MapGet("/documentos/{ordenId}", async (
    string ordenId,
    FacturacionDbContext contexto,
    CancellationToken cancelacion) =>
{
    var documento = await contexto.DocumentosFiscales
        .AsNoTracking()
        .Include(d => d.Lineas)
        .FirstOrDefaultAsync(d => d.OrdenId == ordenId, cancelacion);

    return documento is null
        ? Results.Problem(
            title: "Documento fiscal no encontrado",
            detail: $"No hay un documento fiscal para la orden {ordenId}.",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["codigo"] = "documento_no_encontrado" })
        : Results.Ok(DocumentoFiscalDto.Desde(documento));
});

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
    contexto.Database.Migrate();
}

app.Run();
