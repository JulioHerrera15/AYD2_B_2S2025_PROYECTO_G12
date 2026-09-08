namespace SgoLogic.Contracts.Eventos;

/// <summary>
/// Evento derivado de ejemplo: lo publica SgoLogic.Sample.Worker despues de
/// consumir EjemploCreadoEvento, para demostrar la propagacion de transactionId y
/// causationId a traves de dos saltos. No representa un evento real de ningun
/// contexto.
/// </summary>
public sealed record EjemploProcesadoData(string Nombre, string Mensaje);

public static class EjemploProcesadoEvento
{
    public const string TipoEvento = "EjemploProcesado";
    public const string TipoAgregado = EjemploCreadoEvento.TipoAgregado;
}