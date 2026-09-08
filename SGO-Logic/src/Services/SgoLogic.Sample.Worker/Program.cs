using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Sample.Worker.Consumers;
using SgoLogic.Sample.Worker.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddDbContext<SampleWorkerDbContext>(opciones =>
    opciones.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSgoMessaging();
builder.Services.AddOutbox<SampleWorkerDbContext>(builder.Configuration);
builder.Services.AddHostedService<EjemploCreadoConsumidor>();

var app = builder.Build();

app.UseSgoDefaults();

using (var scope = app.Services.CreateScope())
{
    var contexto = scope.ServiceProvider.GetRequiredService<SampleWorkerDbContext>();
    contexto.Database.Migrate();
}

app.Run();