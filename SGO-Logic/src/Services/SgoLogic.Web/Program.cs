using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

var gatewayUrl = builder.Configuration["ApiGateway:BaseUrl"]
    ?? throw new InvalidOperationException("ApiGateway:BaseUrl no esta configurado");

builder.Services.AddHttpClient<GatewayClient>(http =>
{
    http.BaseAddress = new Uri(gatewayUrl);
    http.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

app.UseSgoDefaults();

app.UseStaticFiles();

app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
