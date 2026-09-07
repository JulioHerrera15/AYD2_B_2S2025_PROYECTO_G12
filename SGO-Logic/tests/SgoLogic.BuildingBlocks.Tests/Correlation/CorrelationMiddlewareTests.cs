using Microsoft.AspNetCore.Http;
using SgoLogic.BuildingBlocks.Correlation;

namespace SgoLogic.BuildingBlocks.Tests.Correlation;

public class CorrelationMiddlewareTests
{
    private static CorrelationMiddleware CrearMiddleware(RequestDelegate siguiente) => new(siguiente);

    [Fact]
    public async Task Genera_transactionId_nuevo_cuando_no_viene_el_header()
    {
        var contexto = new DefaultHttpContext();
        string? transactionIdCapturado = null;

        var middleware = CrearMiddleware(_ =>
        {
            transactionIdCapturado = EventContext.TransactionId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(contexto);

        Assert.False(string.IsNullOrWhiteSpace(transactionIdCapturado));
        Assert.True(Guid.TryParse(transactionIdCapturado, out _));
        Assert.Equal(transactionIdCapturado, contexto.Response.Headers[CorrelationMiddleware.EncabezadoTransactionId]);
    }

    [Fact]
    public async Task Respeta_el_transactionId_que_viene_en_el_header()
    {
        var transactionIdEntrante = Guid.NewGuid().ToString();
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers[CorrelationMiddleware.EncabezadoTransactionId] = transactionIdEntrante;

        string? transactionIdCapturado = null;

        var middleware = CrearMiddleware(_ =>
        {
            transactionIdCapturado = EventContext.TransactionId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(contexto);

        Assert.Equal(transactionIdEntrante, transactionIdCapturado);
        Assert.Equal(transactionIdEntrante, contexto.Response.Headers[CorrelationMiddleware.EncabezadoTransactionId]);
    }
}