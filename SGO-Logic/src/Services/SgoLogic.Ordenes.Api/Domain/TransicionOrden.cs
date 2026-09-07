using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Registro historico de un cambio de estado. De aqui sale el evento
/// OrdenEstadoCambiado que consumen Auditoria, Consulta de Estado y Notificaciones.
/// </summary>
public sealed class TransicionOrden
{
    private TransicionOrden()
    {
        OrdenId = string.Empty;
    }

    public TransicionOrden(
        string ordenId,
        int secuencia,
        EstadoOrden estadoAnterior,
        EstadoOrden estadoNuevo,
        string? motivo,
        PasoSaga? pasoSaga,
        bool esCompensacion,
        DateTime cambiadoEn)
    {
        Id = Guid.NewGuid();
        OrdenId = ordenId;
        Secuencia = secuencia;
        EstadoAnterior = estadoAnterior;
        EstadoNuevo = estadoNuevo;
        Motivo = motivo;
        PasoSaga = pasoSaga;
        EsCompensacion = esCompensacion;
        CambiadoEn = cambiadoEn;
    }

    public Guid Id { get; private set; }

    public string OrdenId { get; private set; }

    public int Secuencia { get; private set; }

    public EstadoOrden EstadoAnterior { get; private set; }

    public EstadoOrden EstadoNuevo { get; private set; }

    public string? Motivo { get; private set; }

    public PasoSaga? PasoSaga { get; private set; }

    public bool EsCompensacion { get; private set; }

    public DateTime CambiadoEn { get; private set; }
}
