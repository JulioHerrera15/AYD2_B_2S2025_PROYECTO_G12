using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;
using Serilog;
using Serilog.Formatting.Json;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.ErrorHandling;
using SgoLogic.BuildingBlocks.HealthChecks;
using SgoLogic.BuildingBlocks.Logging;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.BuildingBlocks.Extensions;

/// <summary>
/// Registra en una sola llamada los transversales comunes a todo servicio SGO-Logic:
/// logging JSON con transactionId, serializacion compartida y health checks.
/// El middleware y los endpoints de health se activan luego con UseSgoDefaults().
/// </summary>
public static class SgoDefaultsExtensions
{
    public static WebApplicationBuilder AddSgoDefaults(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((_, configuracionLog) =>
        {
            configuracionLog
                .Enrich.With<TransactionIdEnricher>()
                .WriteTo.Console(new JsonFormatter(renderMessage: true));
        });

        builder.Services.ConfigureHttpJsonOptions(opciones =>
            SgoJsonSerializerOptions.AplicarA(opciones.SerializerOptions));

        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(opciones =>
            SgoJsonSerializerOptions.AplicarA(opciones.JsonSerializerOptions));

        builder.Services.AddSgoHealthChecks(builder.Configuration);

        return builder;
    }

    public static WebApplication UseSgoDefaults(this WebApplication app)
    {
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseHttpMetrics();

        app.MapSgoHealthChecks();
        app.MapMetrics();

        return app;
    }
}