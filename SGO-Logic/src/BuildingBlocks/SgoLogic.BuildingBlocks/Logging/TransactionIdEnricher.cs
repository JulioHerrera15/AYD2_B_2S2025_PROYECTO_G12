using Serilog.Core;
using Serilog.Events;
using SgoLogic.BuildingBlocks.Correlation;

namespace SgoLogic.BuildingBlocks.Logging;

/// <summary>
/// Agrega el transactionId del EventContext actual a cada evento de log, para que
/// toda la salida quede correlacionada por solicitud.
/// </summary>
public sealed class TransactionIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var transactionId = EventContext.TransactionId;
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("transactionId", transactionId));
    }
}