namespace SgoLogic.IntegracionPos.Worker.Application.Pos;

public sealed class ServicioMapeoPos : IServicioMapeoPos
{
    private readonly string _defaultTienda;

    public ServicioMapeoPos(string defaultTienda = "T01")
    {
        _defaultTienda = string.IsNullOrWhiteSpace(defaultTienda) ? "T01" : defaultTienda.Trim();
    }

    public string ResolverPosInstanciaId(string? tiendaDestinoId)
    {
        var raw = string.IsNullOrWhiteSpace(tiendaDestinoId) ? _defaultTienda : tiendaDestinoId.Trim();
        var normalizada = raw.ToUpperInvariant();

        if (normalizada.StartsWith("TDA-", StringComparison.Ordinal))
        {
            normalizada = normalizada[4..];
        }

        if (normalizada.StartsWith("T", StringComparison.Ordinal) && normalizada.Length > 1)
        {
            return normalizada;
        }

        if (int.TryParse(normalizada, out var numero))
        {
            return $"T{numero:D2}";
        }

        return _defaultTienda;
    }
}
