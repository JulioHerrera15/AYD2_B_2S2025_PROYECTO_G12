using SgoLogic.BuildingBlocks.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSgoDefaults();

app.MapReverseProxy();

app.Run();
