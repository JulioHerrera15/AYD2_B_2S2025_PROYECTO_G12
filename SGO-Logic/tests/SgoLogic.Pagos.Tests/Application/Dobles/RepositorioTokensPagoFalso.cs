using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Tests.Application.Dobles;

/// <summary>Doble en memoria de <see cref="IRepositorioTokensPago" />.</summary>
public sealed class RepositorioTokensPagoFalso : IRepositorioTokensPago
{
    private readonly List<TokenPago> _tokens = new();

    public IReadOnlyList<TokenPago> Todos => _tokens;

    public int Guardados { get; private set; }

    public RepositorioTokensPagoFalso Precargar(params TokenPago[] tokens)
    {
        _tokens.AddRange(tokens);
        return this;
    }

    public Task<TokenPago?> ObtenerAsync(string id, CancellationToken cancelacion) =>
        Task.FromResult(_tokens.FirstOrDefault(t => t.Id == id));

    public Task AgregarAsync(TokenPago token, CancellationToken cancelacion)
    {
        _tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TokenPago>> ListarActivosVencidosAsync(DateTime ahora, int maximo, CancellationToken cancelacion) =>
        Task.FromResult<IReadOnlyList<TokenPago>>(_tokens
            .Where(t => t.Estado == EstadoTokenPago.ACTIVO && t.ExpiraEn <= ahora)
            .OrderBy(t => t.ExpiraEn)
            .Take(maximo)
            .ToList());

    public Task GuardarCambiosAsync(CancellationToken cancelacion)
    {
        Guardados++;
        return Task.CompletedTask;
    }
}
