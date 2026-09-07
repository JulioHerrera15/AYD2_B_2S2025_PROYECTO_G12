using Microsoft.AspNetCore.Http;

namespace SgoLogic.BuildingBlocks.Correlation;

/// <summary>
/// Puebla el transactionId de la solicitud: toma el header X-Transaction-Id si
/// viene, o genera un UUID v4 nuevo; siempre lo devuelve en el header de respuesta.
/// </summary>
public sealed class CorrelationMiddleware
{
    public const string EncabezadoTransactionId = "X-Transaction-Id";

    private readonly RequestDelegate _siguiente;

    public CorrelationMiddleware(RequestDelegate siguiente)
    {
        _siguiente = siguiente;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        var transactionId = contexto.Request.Headers.TryGetValue(EncabezadoTransactionId, out var valores)
            && !string.IsNullOrWhiteSpace(valores.ToString())
                ? valores.ToString()
                : Guid.NewGuid().ToString();

        EventContext.TransactionId = transactionId;
        contexto.Response.Headers[EncabezadoTransactionId] = transactionId;

        await _siguiente(contexto);
    }
}