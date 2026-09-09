using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Entrada del endpoint generico de transiciones, pensado para recorrer el resto
/// de la maquina de estados desde la demo. La validacion de si la transicion es
/// legal la hace el dominio (MaquinaEstadosOrden).
/// </summary>
public sealed record TransicionarOrdenRequest(
    EstadoOrden EstadoDestino,
    PasoSaga? PasoSaga = null,
    string? Motivo = null,
    bool EsCompensacion = false);
