namespace SgoLogic.IntegracionPos.Worker.Application.Pos;

public interface IClientePos
{
    Task<RespuestaPos> EnviarAsync(
        string tiendaId,
        string posInstanciaId,
        string eventoId,
        string ordenId,
        string tipo,
        decimal total,
        CancellationToken cancelacion = default);
}

public sealed record RespuestaPos(
    bool Aceptado,
    bool Duplicado,
    string Estado,
    int? CodigoHttp,
    string? Mensaje,
    DateTime? ProcesadoEn,
    string? RespuestaRaw);
