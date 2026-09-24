using SgoLogic.Pagos.Api.Application.Pagos;

namespace SgoLogic.Pagos.Api.Infrastructure.PayKlik;

/// <summary>
/// Decorador de <see cref="IClientePayKlik" /> que envuelve la llamada HTTP a
/// PayKlik con el disyuntor (circuit breaker) de ADR-009. El circuit breaker
/// envuelve SOLO la llamada al proveedor; los reintentos con backoff los aplica el
/// servicio de cobro alrededor de "registrar intento + invocar".
/// </summary>
public sealed class ClientePayKlikConDisyuntor : IClientePayKlik
{
    private readonly ClientePayKlikHttp _interno;
    private readonly DisyuntorPayKlik _disyuntor;

    public ClientePayKlikConDisyuntor(ClientePayKlikHttp interno, DisyuntorPayKlik disyuntor)
    {
        _interno = interno;
        _disyuntor = disyuntor;
    }

    public Task<ResultadoPayKlik> CargarAsync(
        PeticionCargoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion) =>
        _disyuntor.EjecutarAsync(
            token => _interno.CargarAsync(peticion, claveIdempotencia, token),
            cancelacion);

    public Task<ResultadoPayKlik> ReembolsoAsync(
        PeticionReembolsoPayKlik peticion,
        string claveIdempotencia,
        CancellationToken cancelacion) =>
        _disyuntor.EjecutarAsync(
            token => _interno.ReembolsoAsync(peticion, claveIdempotencia, token),
            cancelacion);
}
