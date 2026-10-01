namespace SgoLogic.Pagos.Api.Application.Tarjetas;

public enum EstadoTokenBoveda
{
    Emitido,

    /// <summary>PayKlik respondio 404: la referencia ya no existe en su boveda.</summary>
    ReferenciaNoExiste,

    /// <summary>PayKlik respondio 422 (CVV invalido, tarjeta vencida).</summary>
    Rechazado,

    /// <summary>Sin respuesta, timeout o 5xx.</summary>
    NoDisponible
}

public sealed record ResultadoTokenBoveda(EstadoTokenBoveda Estado, TokenTarjetaDto? Token = null, string? Mensaje = null);

/// <summary>
/// Puerto hacia POST /v1/tarjetas/tokens de PayKlik: token de un solo uso desde una
/// referencia de boveda y el CVV del cliente. El CVV no se registra ni se guarda.
/// </summary>
public interface IClienteBovedaPayKlik
{
    Task<ResultadoTokenBoveda> GenerarTokenAsync(string referenciaTarjeta, string cvv, CancellationToken cancelacion);
}
