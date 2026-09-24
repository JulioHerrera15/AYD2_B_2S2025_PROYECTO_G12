namespace SgoLogic.BuildingBlocks.Idempotencia;

/// <summary>
/// Marca de un eventId ya procesado por este servicio. Vive en la base de cada
/// contexto (igual que outbox_messages), nunca centralizada. El indice unico
/// compuesto (EventId, NombreServicio) es lo que hace la deduplicacion atomica:
/// dos intentos concurrentes de insertar la misma marca chocan en la base, no en
/// memoria.
/// </summary>
public sealed class MensajeProcesado
{
    private MensajeProcesado()
    {
    }

    public MensajeProcesado(string eventId, string nombreServicio, DateTime fechaProceso)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        NombreServicio = nombreServicio;
        FechaProceso = fechaProceso;
    }

    public Guid Id { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public string NombreServicio { get; private set; } = string.Empty;
    public DateTime FechaProceso { get; private set; }
}