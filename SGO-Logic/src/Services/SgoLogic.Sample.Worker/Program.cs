using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.Sample.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.UseSgoDefaults();

app.Run();