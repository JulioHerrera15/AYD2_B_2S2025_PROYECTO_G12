using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Application.Ordenes;

/// <summary>
/// Transicion que la saga aplica justo despues de otra, en la misma transaccion
/// (ver <see cref="ServicioConfirmacionOrden.AplicarTransicionAsync" />). Son los
/// tramos que antes solo avanzaban si alguien llamaba a POST /transiciones.
/// </summary>
public sealed record TransicionEncadenada(
    EstadoOrden EstadoDestino,
    PasoSaga? PasoSaga,
    string? Motivo);
