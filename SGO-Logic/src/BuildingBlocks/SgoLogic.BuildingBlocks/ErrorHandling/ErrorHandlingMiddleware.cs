using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SgoLogic.BuildingBlocks.Correlation;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.BuildingBlocks.ErrorHandling;

/// <summary>
/// Captura cualquier excepcion no controlada y responde con ProblemDetails,
/// incluyendo el transactionId de la solicitud para poder correlacionar el error
/// con los logs.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate siguiente, ILogger<ErrorHandlingMiddleware> logger)
    {
        _siguiente = siguiente;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _siguiente(contexto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado procesando la solicitud {Ruta}", contexto.Request.Path);

            var problema = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Ocurrio un error interno",
                Detail = "Ocurrio un error inesperado al procesar la solicitud.",
                Instance = contexto.Request.Path
            };
            problema.Extensions["transactionId"] = EventContext.TransactionId;

            contexto.Response.ContentType = "application/problem+json";
            contexto.Response.StatusCode = problema.Status!.Value;

            await contexto.Response.WriteAsJsonAsync(problema, SgoJsonSerializerOptions.Default);
        }
    }
}