using SgoLogic.BuildingBlocks.Extensions;
using SgoLogic.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddSgoDefaults();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

var gatewayUrl = builder.Configuration["ApiGateway:BaseUrl"]
    ?? throw new InvalidOperationException("ApiGateway:BaseUrl no esta configurado");

// Los encabezados de sesion (Authorization y X-Session-Id) los pone el handler en
// cada mensaje: GatewayClient nunca toca DefaultRequestHeaders.
builder.Services.AddTransient<EncabezadosSesionHandler>();
builder.Services.AddHttpClient<GatewayClient>(http =>
{
    http.BaseAddress = new Uri(gatewayUrl);
    http.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<EncabezadosSesionHandler>();

var app = builder.Build();

app.UseSgoDefaults();

app.UseStaticFiles();

app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
