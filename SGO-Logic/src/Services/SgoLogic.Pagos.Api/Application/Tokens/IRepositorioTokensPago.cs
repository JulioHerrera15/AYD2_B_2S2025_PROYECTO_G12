using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tokens;

/// <summary>Puerto de persistencia de <see cref="TokenPago" /> (tabla pagos.token_pago).</summary>
public interface IRepositorioTokensPago
{
    Task<TokenPago?> ObtenerAsync(string id, CancellationToken cancelacion);

    Task AgregarAsync(TokenPago token, CancellationToken cancelacion);

    /// <summary>Tokens ACTIVO cuyo ExpiraEn ya paso, hasta <paramref name="maximo" />.</summary>
    Task<IReadOnlyList<TokenPago>> ListarActivosVencidosAsync(DateTime ahora, int maximo, CancellationToken cancelacion);

    Task GuardarCambiosAsync(CancellationToken cancelacion);
}
